using System.Net;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Extensions;
using IdentityService.Interfaces;
using IdentityService.Models;
using IdentityService.Utilities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly IdentityDbContext _context;
    private readonly IConfiguration _config;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<RefreshTokenService> _logger;
    private readonly ITokenVersionStore _tokenVersionStore;

    public RefreshTokenService(
        IConfiguration config,
        IdentityDbContext context,
        UserManager<ApplicationUser> userManager,
        ILogger<RefreshTokenService> logger,
        ITokenVersionStore tokenVersionStore
    )
    {
        _context = context;
        _config = config;
        _userManager = userManager;
        _logger = logger;
        _tokenVersionStore = tokenVersionStore;
    }

    public Task<string> GenerateRefreshTokenAsync()
    {
        // Güvenli refresh token oluştur
        var randomBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Task.FromResult(Convert.ToBase64String(randomBytes));
    }

    public async Task<ApiResponse<RefreshTokenResultDto>> CreateUserRefreshTokenAsync(
        string userId,
        string deviceInfo,
        string ipAddress
    )
    {
        try
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User not found: {UserId}", userId);
                return ApiResponse<RefreshTokenResultDto>.Failed(
                    "Acccess token creation failed - User does not exist",
                    new List<string> { "User does not exist" },
                    (int)HttpStatusCode.NotFound
                );
            }

            // Gateway oturumu yalnızca Redis'teki sürüm üzerinden doğrular. Redis
            // hazır değilse doğrulanamayacak bir JWT üretmeyiz.
            if (!await _tokenVersionStore.SetAtLeastAsync(user.Id, user.TokenVersion))
            {
                return ApiResponse<RefreshTokenResultDto>.Failed(
                    "Session service is temporarily unavailable.",
                    statusCode: (int)HttpStatusCode.ServiceUnavailable
                );
            }

            // Kullanıcının tüm tokenlerini al
            var userTokens = await _context
                .UserRefreshTokens.Where(rt => rt.UserId == userId)
                .ToListAsync();

            if (userTokens.Count > 0)
            {
                var expiredTokens = userTokens
                .Where(rt => rt.RefreshTokenExpiryTime < DateTime.UtcNow)
                .ToList();
            if (expiredTokens.Any())
            {
                _context.UserRefreshTokens.RemoveRange(expiredTokens);
            }

            var activeTokens = userTokens
                .Where(rt => rt.RefreshTokenExpiryTime >= DateTime.UtcNow)
                .ToList();
            if (activeTokens.Count >= 5)
            {
                // En eski tokeni sil
                var tokenToRemove =
                    activeTokens.FirstOrDefault(x => x.DeviceInfo == deviceInfo)
                    ?? activeTokens.First();

                if (tokenToRemove != null)
                {
                    _context.UserRefreshTokens.Remove(tokenToRemove);
                }
            }
            }
            
            // Yeni token oluştur
            var refreshToken = new UserRefreshToken
            {
                Id = Guid.NewGuid().ToString(),
                RefreshToken = await GenerateRefreshTokenAsync(),
                RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7),
                CreatedAt = DateTime.UtcNow,
                CreatedByIp = ipAddress ?? "unknown",
                DeviceInfo = deviceInfo ?? "unknown",
                UserId = user.Id,
            };

            _context.UserRefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();
            // Yeni access token oluştur
            var accessToken = _config.GenerateJwtToken(user);

            return ApiResponse<RefreshTokenResultDto>.Success(
                new RefreshTokenResultDto
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken.RefreshToken,
                },
                "Refresh token created successfully."
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating refresh token for user: {UserId}", userId);
            return ApiResponse<RefreshTokenResultDto>.Failed(
                "An error occurred while creating refresh token",
                new List<string> { ex.Message },
                (int)HttpStatusCode.InternalServerError
            );
        }
    }

    public async Task<UserRefreshToken> GetValidRefreshTokenAsync(string refreshToken)
    {
        return await _context
            .UserRefreshTokens.Include(rt => rt.User)
            .FirstOrDefaultAsync(rt =>
                rt.RefreshToken == refreshToken && rt.RefreshTokenExpiryTime > DateTime.UtcNow
            );
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken)
    {
        try
        {
            var token = await _context.UserRefreshTokens.FirstOrDefaultAsync(rt =>
                rt.RefreshToken == refreshToken
            );

            if (token != null)
            {
                _context.UserRefreshTokens.Remove(token);
                await _context.SaveChangesAsync();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking refresh token");
            return false;
        }
    }

    public async Task<bool> RevokeAllUserTokensAsync(string userId)
    {
        try
        {
            var userTokens = await _context
                .UserRefreshTokens.Where(rt => rt.UserId == userId)
                .ToListAsync();

            _context.UserRefreshTokens.RemoveRange(userTokens);
            await _context.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking all tokens for user: {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> InvalidateAllUserSessionsAsync(string userId)
    {
        try
        {
            var databaseVersion = await _context
                .Users.AsNoTracking()
                .Where(user => user.Id == userId)
                .Select(user => (int?)user.TokenVersion)
                .SingleOrDefaultAsync();
            if (!databaseVersion.HasValue)
            {
                return false;
            }

            // Redis önce artırılır. Sonraki DB işlemi başarısız olsa dahi eski
            // access token'lar Gateway'de yeniden geçerli hale gelemez.
            var nextVersion = await _tokenVersionStore.IncrementAsync(
                userId,
                databaseVersion.Value
            );
            if (!nextVersion.HasValue)
            {
                return false;
            }

            if (_context.Database.IsRelational())
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();

                await _context
                    .Users.Where(user =>
                        user.Id == userId && user.TokenVersion < nextVersion.Value
                    )
                    .ExecuteUpdateAsync(setters =>
                        setters.SetProperty(
                            user => user.TokenVersion,
                            nextVersion.Value
                        )
                    );

                await _context
                    .UserRefreshTokens.Where(token => token.UserId == userId)
                    .ExecuteDeleteAsync();
                await transaction.CommitAsync();
                return true;
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            user.TokenVersion = Math.Max(user.TokenVersion, nextVersion.Value);
            var userTokens = await _context
                .UserRefreshTokens.Where(token => token.UserId == userId)
                .ToListAsync();
            _context.UserRefreshTokens.RemoveRange(userTokens);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating all sessions for user: {UserId}", userId);
            return false;
        }
    }

    public async Task CleanupExpiredTokensAsync()
    {
        try
        {
            var expiredTokens = await _context
                .UserRefreshTokens.Where(rt => rt.RefreshTokenExpiryTime < DateTime.UtcNow)
                .ToListAsync();

            if (expiredTokens.Any())
            {
                _context.UserRefreshTokens.RemoveRange(expiredTokens);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Cleaned up {Count} expired refresh tokens",
                    expiredTokens.Count
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during cleanup of expired tokens");
        }
    }

    private async Task CleanupExpiredUserTokensAsync(string userId)
    {
        var expiredTokens = await _context
            .UserRefreshTokens.Where(rt =>
                rt.UserId == userId && rt.RefreshTokenExpiryTime < DateTime.UtcNow
            )
            .ToListAsync();

        if (expiredTokens.Any())
        {
            _context.UserRefreshTokens.RemoveRange(expiredTokens);
        }
    }
}
