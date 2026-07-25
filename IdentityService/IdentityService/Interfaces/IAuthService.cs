using System;
using IdentityService.DTOs;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Interfaces;

public interface IAuthService
{
    Task<ApiResponse<AuthResponseDto>> LoginAsync(LoginRequestModel model);
    Task<ApiResponse<RefreshTokenResultDto>> RefreshTokenAsync(RefreshTokenDto model);
    Task<ApiResponse<List<UserSessionsResultDto>>> GetMySessionsByUserId(string userId);
    Task<ApiResponse<string>> LogoutSessionAsync(string sessionId, string userId);
}
