using System;
using IdentityService.DTOs;
using IdentityService.Models;

namespace IdentityService.Interfaces;

public interface IRefreshTokenService
{
    Task<string> GenerateRefreshTokenAsync();
    Task<ApiResponse<RefreshTokenResultDto>> CreateUserRefreshTokenAsync(
        string userId,
        string deviceInfo,
        string ipAddress
    );
    Task<UserRefreshToken> GetValidRefreshTokenAsync(string refreshToken);
    Task<bool> RevokeRefreshTokenAsync(string refreshToken);
    Task<bool> RevokeAllUserTokensAsync(string userId);
    Task<bool> InvalidateAllUserSessionsAsync(string userId);
    Task CleanupExpiredTokensAsync();
}
