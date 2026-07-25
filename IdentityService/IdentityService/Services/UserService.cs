// File: IdentityService/Services/UserService.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IdentityService.DTOs;
using IdentityService.Extensions;
using IdentityService.Interfaces;
using IdentityService.Models;
using IdentityService.Utilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdentityService.Services
{
    public class UserService : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _mailService;
        private readonly ILogger<UserService> _logger;
        private readonly IConfiguration _config;

        public UserService(
            UserManager<ApplicationUser> userManager,
            IEmailService mailService,
            ILogger<UserService> logger,
            IConfiguration config
        )
        {
            _userManager = userManager;
            _mailService = mailService;
            _logger = logger;
            _config = config;
        }

        public async Task<IdentityResult> UpdateUserAsync(string userId, UpdateUserModel model)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("Update failed: user with id {UserId} not found", userId);
                return IdentityResult.Failed(new IdentityError { Description = "User not found" });
            }

            user.UserName = model.UserName;
            user.AvatarUrl = model.AvatarUrl;
            user.Bio = model.Bio;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                _logger.LogWarning(
                    "Update failed for user id {UserId}. Errors: {Errors}",
                    userId,
                    string.Join(", ", result.Errors.Select(e => e.Description))
                );
            }
            else
            {
                _logger.LogInformation("Successfully updated user {UserId}", userId);
            }

            return result;
        }

        public async Task<ApiResponse<object>> ChangeEmailAsync(
            string userId,
            ChangeEmailRequestDto model,
            string confirmationUrl
        )
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("Email change failed: user {UserId} not found", userId);
                return ApiResponse<object>.NotFound("User not found");
            }

            var emailChanged = !string.Equals(
                user.Email,
                model.Email,
                System.StringComparison.OrdinalIgnoreCase
            );

            if (emailChanged)
            {
                var existingUser = await _userManager.FindByEmailAsync(model.Email);
                if (existingUser != null && existingUser.Id != user.Id)
                {
                    return ApiResponse<object>.Failed(
                        "Email could not be updated.",
                        new[] { "Email is already taken." }
                    );
                }

                var updateResult = await _userManager.SetEmailAsync(user, model.Email);
                if (!updateResult.Succeeded)
                {
                    _logger.LogWarning(
                        "Email change failed for user {UserId}. Errors: {Errors}",
                        userId,
                        string.Join(", ", updateResult.Errors.Select(e => e.Description))
                    );
                    return ApiResponse<object>.Failed(
                        "Email could not be updated.",
                        updateResult.Errors.Select(e => e.Description)
                    );
                }
            }
            else if (user.EmailConfirmed)
            {
                return ApiResponse<object>.Success(
                    "This email address is already confirmed.",
                    (int)System.Net.HttpStatusCode.OK
                );
            }

            var emailResult = await _mailService.SendEmailConfirmationAsync(
                user.Id,
                confirmationUrl
            );
            if (!emailResult.IsSuccessfull)
            {
                _logger.LogError(
                    "Email was updated but confirmation email could not be sent for user {UserId}.",
                    userId
                );
                return ApiResponse<object>.Failed(
                    "Email was updated, but the confirmation email could not be sent. Please try again.",
                    emailResult.Errors,
                    (int)System.Net.HttpStatusCode.InternalServerError
                );
            }

            _logger.LogInformation("Email changed for user {UserId}; confirmation sent.", userId);
            return ApiResponse<object>.Success(
                "Email updated. Please check your new address for the confirmation link.",
                (int)System.Net.HttpStatusCode.OK
            );
        }

        public async Task<IdentityResult> DeleteUserAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogWarning("Delete failed: user id is empty");
                return IdentityResult.Failed(
                    new IdentityError { Description = "User id cannot be empty" }
                );
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                _logger.LogWarning("Delete failed: user with id {UserId} not found", id);
                return IdentityResult.Failed(new IdentityError { Description = "User not found" });
            }

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                _logger.LogInformation("Successfully deleted user with id {UserId}", id);
            }
            else
            {
                _logger.LogWarning(
                    "Delete failed for user id {UserId}. Errors: {Errors}",
                    id,
                    string.Join(", ", result.Errors.Select(e => e.Description))
                );
            }
            return result;
        }

        public async Task<ApplicationUser> GetUserByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogWarning("GetUser failed: empty user id");
                return null;
            }

            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                {
                    _logger.LogWarning("User not found with id {UserId}", id);
                }
                return user;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error occurred while retrieving user with id {UserId}", id);
                return null;
            }
        }

        public async Task<UserMeDto> GetMeAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return null;
            }

            return new UserMeDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Email = user.Email,
                Bio = user.Bio,
                AvatarUrl = user.AvatarUrl,
                EmailConfirmed = user.EmailConfirmed,
            };
        }

        public async Task<IdentityResult> ChangePasswordAsync(
            string userId,
            string currentPassword,
            string newPassword
        )
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("ChangePassword failed: user with id {UserId} not found", userId);
                return IdentityResult.Failed(new IdentityError { Description = "User not found" });
            }

            var result = await _userManager.ChangePasswordAsync(
                user,
                currentPassword,
                newPassword
            );
            if (result.Succeeded)
            {
                _logger.LogInformation("Password changed successfully for user {UserId}", userId);
            }
            else
            {
                _logger.LogWarning(
                    "ChangePassword failed for user id {UserId}. Errors: {Errors}",
                    userId,
                    string.Join(", ", result.Errors.Select(e => e.Description))
                );
            }
            return result;
        }

        public async Task<List<UserSearchResultDto>> SearchUsersAsync(
            string requestingUserId,
            string query,
            int page,
            int limit
        )
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<UserSearchResultDto>();
            }

            if (page <= 0)
                page = 1;
            if (limit <= 0)
                limit = 20;
            if (limit > 50)
                limit = 50;

            var users = await _userManager
                .Users.Where(u =>
                    u.Id != requestingUserId && EF.Functions.ILike(u.UserName, $"%{query}%")
                )
                .OrderBy(u => u.UserName)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(u => new UserSearchResultDto
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    AvatarUrl = u.AvatarUrl,
                })
                .ToListAsync();

            return users;
        }
    }
}
