using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IdentityService.Models;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Utilities;

public class GenerateToken
{
    public static string GenerateJSONWebToken(ApplicationUser user, IConfiguration _config)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtKey = _config["JWT:Key"] ?? throw new InvalidOperationException("JWT:Key is missing.");
        var issuer = _config["JWT:Issuer"] ?? throw new InvalidOperationException("JWT:Issuer is missing.");
        var audience = _config["JWT:Audience"] ?? throw new InvalidOperationException("JWT:Audience is missing.");
        var key = Encoding.ASCII.GetBytes(jwtKey);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? "Avel"),
            new Claim(JwtRegisteredClaimNames.Picture, user.AvatarUrl ?? ""),
            new Claim("IsEmailConfirmed", user.EmailConfirmed.ToString()),
            new Claim("token_version", user.TokenVersion.ToString()),
        };
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(key),
            SecurityAlgorithms.HmacSha256
        );
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.Now.AddMonths(1),
            signingCredentials: creds
        );
        return tokenHandler.WriteToken(token);
    }

    public static string GenerateRefreshToken()
    {
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return refreshToken;
    }
}
