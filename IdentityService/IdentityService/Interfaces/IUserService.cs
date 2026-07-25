using System;
using System.Collections.Generic;
using IdentityService.DTOs;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Interfaces;

public interface IUserService
{
    Task<IdentityResult> UpdateUserAsync(string userId, UpdateUserModel model);
    Task<ApiResponse<object>> ChangeEmailAsync(
        string userId,
        ChangeEmailRequestDto model,
        string confirmationUrl
    );
    Task <IdentityResult>  DeleteUserAsync(string id);
    Task<ApplicationUser> GetUserByIdAsync(string id);
    Task<UserMeDto> GetMeAsync(string userId);
    Task<IdentityResult> ChangePasswordAsync(string userId, string currentPassword, string newPassword);
    Task<List<UserSearchResultDto>> SearchUsersAsync(string requestingUserId, string query, int page, int limit);
}
