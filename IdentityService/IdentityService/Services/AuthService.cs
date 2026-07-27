using System.Net;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Extensions;
using IdentityService.Interfaces;
using IdentityService.Messaging;
using IdentityService.Models;
using IdentityService.Utilities;
using MassTransit.Initializers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;
    private readonly IIdentityProducer _messagePublisher;
    private readonly ILogger<AuthService> _logger;
    private readonly IIpAddressService _ipAddressService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IdentityDbContext _context;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        IIdentityProducer messagePublisher,
        ILogger<AuthService> logger,
        IdentityDbContext context,
        IIpAddressService ipAddressService,
        IRefreshTokenService refreshTokenService
    )
    {
        _userManager = userManager;
        _config = config;
        _messagePublisher = messagePublisher;
        _logger = logger;
        _ipAddressService = ipAddressService;
        _refreshTokenService = refreshTokenService;
        _context = context;
    }

    public async Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequestModel model)
    {
        try
        {
            var user = await _userManager.FindByNameAsync(model.UserName);

            if (user == null)
            {
                _logger.LogWarning("User not found with username: {0}", model.UserName);
                return ApiResponse<AuthResponseDto>.Failed("There is no user with this username");
            }

            var passwordCheck = await _userManager.CheckPasswordAsync(user, model.Password);

            if (!passwordCheck)
            {
                _logger.LogWarning("Login failed for user: {Username}", model.UserName);
                return ApiResponse<AuthResponseDto>.Failed("Invalid username or password");
            }

            if (await _userManager.IsLockedOutAsync(user))
            {
                return ApiResponse<AuthResponseDto>.Failed(
                    "Account locked",
                    new List<string>
                    {
                        "Account is temporarily locked due to multiple failed attempts",
                    },
                    (int)HttpStatusCode.Unauthorized
                );
            }

            _logger.LogInformation("User logged in: {0}", model.UserName);
            string token = _config.GenerateJwtToken(user);
            var refreshTokenResult = await _refreshTokenService.CreateUserRefreshTokenAsync(
                user.Id,
                model.DeviceInfo,
                _ipAddressService.GetClientIpAddress()
            );

            if (!refreshTokenResult.IsSuccessfull)
            {
                _logger.LogWarning(
                    "Failed to update refresh token for user: {Username}",
                    user.UserName
                );
                return ApiResponse<AuthResponseDto>.Failed(
                    "Login successful but failed to update refresh token",
                    refreshTokenResult.Errors,
                    refreshTokenResult.StatusCode
                );
            }
            var authResponse = new AuthResponseDto
            {
                UserID = user.Id,
                AccessToken = token,
                RefreshToken = refreshTokenResult.Data.RefreshToken,
            };
            return ApiResponse<AuthResponseDto>.Success(authResponse, "Login successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred during login for user: {0}", model.UserName);
            return ApiResponse<AuthResponseDto>.Failed(
                "An error occurred during login",
                new List<string> { ex.Message },
                (int)HttpStatusCode.InternalServerError
            );
        }
    }

    public async Task<ApiResponse<RefreshTokenResultDto>> RefreshTokenAsync(RefreshTokenDto model)
    {
        try
        {
            var refreshToken = await _refreshTokenService.GetValidRefreshTokenAsync(
                model.RefreshToken
            );
            if (refreshToken == null)
            {
                return ApiResponse<RefreshTokenResultDto>.Failed(
                    "Invalid refresh token",
                    null,
                    (int)HttpStatusCode.Unauthorized
                );
            }

            await _refreshTokenService.RevokeRefreshTokenAsync(model.RefreshToken);

            var newRefreshTokenResult = await _refreshTokenService.CreateUserRefreshTokenAsync(
                refreshToken.UserId,
                model.DeviceInfo,
                _ipAddressService.GetClientIpAddress()
            );
            return newRefreshTokenResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred during refresh token for user: {0}",
                model.UserId
            );
            return ApiResponse<RefreshTokenResultDto>.Failed(
                "An error occurred during refresh token",
                new List<string> { ex.Message },
                (int)HttpStatusCode.InternalServerError
            );
        }
    }

    public async Task<ApiResponse<List<UserSessionsResultDto>>> GetMySessionsByUserId(string userId)
    {
        var sessions = await _context
            .UserRefreshTokens.AsNoTracking()
            .Where(rt => rt.UserId == userId && rt.RefreshTokenExpiryTime > DateTime.UtcNow)
            .Select(rt => new UserSessionsResultDto
            {
                Id = rt.Id,
                DeviceInfo = rt.DeviceInfo,
                CreatedAt = rt.CreatedAt,
                CreatedByIp = rt.CreatedByIp,
            })
            .ToListAsync();

        if (!sessions.Any())
        {
            return ApiResponse<List<UserSessionsResultDto>>.Failed(
                "No sessions found for this user",
                null,
                (int)HttpStatusCode.NotFound
            );
        }
        return ApiResponse<List<UserSessionsResultDto>>.Success(
            sessions,
            "User sessions retrieved successfully",
            (int)HttpStatusCode.OK
        );
    }

    public async Task<ApiResponse<string>> LogoutSessionAsync(string sessionId, string userId)
    {
        try
        {
            var session = await _context.UserRefreshTokens.FindAsync(sessionId);
            if (session == null || session.UserId != userId)
            {
                return ApiResponse<string>.Failed(
                    "Session not found",
                    null,
                    (int)HttpStatusCode.NotFound
                );
            }
            if (!session.IsActive)
            {
                return ApiResponse<string>.Failed(
                    "Session is already inactive",
                    null,
                    (int)HttpStatusCode.BadRequest
                );
            }
            _context.UserRefreshTokens.Remove(session);
            var result = await _context.SaveChangesAsync();
            return ApiResponse<string>.Success(
                "",
                "Session logged out successfully",
                (int)HttpStatusCode.OK
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An error occurred while logging out session with ID: {0}",
                sessionId
            );
            return ApiResponse<string>.Failed(
                "An error occurred while logging out session",
                new List<string> { ex.Message },
                (int)HttpStatusCode.InternalServerError
            );
        }
    }
}
