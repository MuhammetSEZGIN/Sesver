using System;
using IdentityService.DTOs;
using IdentityService.Models;

namespace IdentityService.Interfaces;

public interface IEmailService
{
    Task<ApiResponse<object>> SendEmailAsync(string toEmail, string subject, string content);
    Task<ApiResponse<object>> ConfirmEmail(string userId, string token);
    Task<ApiResponse<object>> SendEmailConfirmationAsync(string userId, string confirmationUrl);
    Task<ApiResponse<object>> SendPasswordResetAsync(string email);
    Task<ApiResponse<object>> ResetPasswordAsync(ResetPasswordRequestDto model);
}
