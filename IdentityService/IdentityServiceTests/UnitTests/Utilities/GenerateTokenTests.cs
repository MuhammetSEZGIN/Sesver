using Xunit;
using IdentityService.Utilities;
using IdentityService.Models;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;

namespace IdentityServiceTests.UnitTests.Utilities; 
public class GenerateTokenTests
{
    [Fact]
    public void GenerateToken_ReturnsValidToken()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "JWT:Key", "test-signing-key-12345678901234567890" },
                { "JWT:Issuer", "test-issuer" },
                { "JWT:Audience", "test-audience" }
            })
            .Build();
     
          var testUser = new ApplicationUser
        {
            Id = "user-123",
            UserName = "testuser",
            EmailConfirmed = true,
            AvatarUrl = "https://example.com/avatar.png",
            TokenVersion = 7
        };
        var token = GenerateToken.GenerateJSONWebToken(testUser, config);
        Assert.NotNull(token);
        Assert.True(token.Length > 0);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("7", jwt.Claims.Single(claim => claim.Type == "token_version").Value);
    }
}
