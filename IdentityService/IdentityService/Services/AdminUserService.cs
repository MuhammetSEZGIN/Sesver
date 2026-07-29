using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public sealed class AdminUserService : IAdminUserService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IdentityDbContext _context;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly ILogger<AdminUserService> _logger;

    public AdminUserService(
        UserManager<ApplicationUser> userManager,
        IdentityDbContext context,
        IRefreshTokenService refreshTokenService,
        ILogger<AdminUserService> logger)
    {
        _userManager = userManager;
        _context = context;
        _refreshTokenService = refreshTokenService;
        _logger = logger;
    }

    public async Task<AdminUserPageDto> GetUsersAsync(string query, int page, int limit)
    {
        page = page < 1 ? 1 : page;
        limit = limit < 1 ? DefaultPageSize : Math.Min(limit, MaximumPageSize);

        var users = _userManager.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var searchTerm = query.Trim().ToUpperInvariant();
            users = users.Where(user =>
                user.Id.ToUpper().Contains(searchTerm)
                || (user.NormalizedUserName != null
                    && user.NormalizedUserName.Contains(searchTerm))
                || (user.NormalizedEmail != null
                    && user.NormalizedEmail.Contains(searchTerm)));
        }

        var totalCount = await users.CountAsync();
        var items = await users
            .OrderBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Skip((page - 1) * limit)
            .Take(limit)
            .Select(user => new AdminUserListItemDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                EmailConfirmed = user.EmailConfirmed,
            })
            .ToListAsync();

        return new AdminUserPageDto
        {
            Items = items,
            Page = page,
            Limit = limit,
            TotalCount = totalCount,
        };
    }

    public async Task<ApiResponse<object>> DeleteUserAsync(
        string actorUserId,
        string targetUserId)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            return ApiResponse<object>.Failed("User id cannot be empty.");
        }

        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
        {
            return ApiResponse<object>.Failed("You cannot delete your own admin account.");
        }

        var user = await _userManager.FindByIdAsync(targetUserId);
        if (user == null)
        {
            return ApiResponse<object>.NotFound("User not found.");
        }

        // Access token'lar Gateway'de Redis token surumuyle dogrulanir. Hesabi
        // silmeden once surumu artirarak mevcut tum oturumlari aninda gecersiz kil.
        if (!await _refreshTokenService.InvalidateAllUserSessionsAsync(targetUserId))
        {
            _logger.LogWarning(
                "Admin user deletion aborted because sessions could not be invalidated for {TargetUserId}",
                targetUserId);
            return ApiResponse<object>.Failed(
                "User sessions could not be invalidated.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // AddresseeId intentionally uses Restrict in the model, so friendships must
        // be removed before ASP.NET Identity deletes the user. They are tracked in
        // the same scoped DbContext and committed by UserManager.DeleteAsync.
        var friendships = await _context.Friendships
            .Where(friendship =>
                friendship.RequesterId == targetUserId
                || friendship.AddresseeId == targetUserId)
            .ToListAsync();
        _context.Friendships.RemoveRange(friendships);

        try
        {
            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(error => error.Description).ToArray();
                _logger.LogWarning(
                    "Admin user deletion failed for {TargetUserId}. Errors: {Errors}",
                    targetUserId,
                    string.Join(", ", errors));
                return ApiResponse<object>.Failed("User could not be deleted.", errors);
            }
        }
        catch (DbUpdateException exception)
        {
            _logger.LogError(
                exception,
                "Admin user deletion failed while persisting {TargetUserId}",
                targetUserId);
            return ApiResponse<object>.Failed("User could not be deleted.");
        }

        _logger.LogInformation(
            "Admin {ActorUserId} deleted user {TargetUserId}",
            actorUserId,
            targetUserId);
        return ApiResponse<object>.Success(
            "User deleted successfully.",
            StatusCodes.Status204NoContent);
    }
}
