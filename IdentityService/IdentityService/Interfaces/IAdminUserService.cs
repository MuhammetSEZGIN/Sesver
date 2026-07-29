using IdentityService.DTOs;

namespace IdentityService.Interfaces;

public interface IAdminUserService
{
    Task<AdminUserPageDto> GetUsersAsync(string query, int page, int limit);
    Task<ApiResponse<object>> DeleteUserAsync(string actorUserId, string targetUserId);
}
