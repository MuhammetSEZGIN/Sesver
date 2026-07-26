using System.Net;
using System.Text;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace IdentityServiceTests.UnitTests.Services;

public class EmailServiceTests
{
    private static Mock<UserManager<ApplicationUser>> BuildUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
    }

    private static EmailService CreateService(
        Mock<UserManager<ApplicationUser>> userManager,
        Mock<IRefreshTokenService> refreshTokenService,
        IDictionary<string, string?> configuration = null
    )
    {
        configuration ??= new Dictionary<string, string?>
        {
            ["Smtp:Enabled"] = "false",
            ["ClientApp:PasswordResetUrl"] = "http://localhost:5173/reset-password",
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(configuration).Build();
        return new EmailService(
            config,
            Mock.Of<ILogger<EmailService>>(),
            userManager.Object,
            Mock.Of<IIpAddressService>(),
            refreshTokenService.Object
        );
    }

    [Fact]
    public async Task SendPasswordResetAsync_UnknownEmail_ReturnsGenericSuccess()
    {
        var userManager = BuildUserManager();
        userManager
            .Setup(x => x.FindByEmailAsync("unknown@example.com"))
            .ReturnsAsync((ApplicationUser)null);
        var service = CreateService(userManager, new Mock<IRefreshTokenService>());

        var result = await service.SendPasswordResetAsync("unknown@example.com");

        Assert.True(result.IsSuccessfull);
        Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(
            "If an account exists for this email, a password reset link has been sent.",
            result.Message
        );
        userManager.Verify(
            x => x.GeneratePasswordResetTokenAsync(It.IsAny<ApplicationUser>()),
            Times.Never
        );
    }

    [Fact]
    public async Task SendPasswordResetAsync_KnownEmail_GeneratesResetToken()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "testuser",
            Email = "test@example.com",
        };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        userManager
            .Setup(x => x.GeneratePasswordResetTokenAsync(user))
            .ReturnsAsync("reset-token");
        var service = CreateService(userManager, new Mock<IRefreshTokenService>());

        var result = await service.SendPasswordResetAsync(user.Email);

        Assert.True(result.IsSuccessfull);
        Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        userManager.Verify(x => x.GeneratePasswordResetTokenAsync(user), Times.Once);
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_ResetsPasswordAndRevokesSessions()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "test@example.com" };
        const string identityToken = "identity/reset+token==";
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(identityToken));
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        userManager
            .Setup(x => x.ResetPasswordAsync(user, identityToken, "NewPass@123"))
            .ReturnsAsync(IdentityResult.Success);
        var refreshTokenService = new Mock<IRefreshTokenService>();
        refreshTokenService
            .Setup(x => x.InvalidateAllUserSessionsAsync(user.Id))
            .ReturnsAsync(true);
        var service = CreateService(userManager, refreshTokenService);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto
            {
                Email = user.Email,
                Token = encodedToken,
                NewPassword = "NewPass@123",
                NewPasswordConfirmation = "NewPass@123",
            }
        );

        Assert.True(result.IsSuccessfull);
        Assert.Equal((int)HttpStatusCode.OK, result.StatusCode);
        userManager.Verify(
            x => x.ResetPasswordAsync(user, identityToken, "NewPass@123"),
            Times.Once
        );
        refreshTokenService.Verify(
            x => x.InvalidateAllUserSessionsAsync(user.Id),
            Times.Once
        );
    }

    [Fact]
    public async Task ResetPasswordAsync_MalformedToken_ReturnsBadRequest()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "test@example.com" };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByEmailAsync(user.Email)).ReturnsAsync(user);
        var refreshTokenService = new Mock<IRefreshTokenService>();
        var service = CreateService(userManager, refreshTokenService);

        var result = await service.ResetPasswordAsync(
            new ResetPasswordRequestDto
            {
                Email = user.Email,
                Token = "%%%not-base64%%%",
                NewPassword = "NewPass@123",
                NewPasswordConfirmation = "NewPass@123",
            }
        );

        Assert.False(result.IsSuccessfull);
        Assert.Equal((int)HttpStatusCode.BadRequest, result.StatusCode);
        userManager.Verify(
            x => x.ResetPasswordAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<string>()
            ),
            Times.Never
        );
        refreshTokenService.Verify(
            x => x.InvalidateAllUserSessionsAsync(It.IsAny<string>()),
            Times.Never
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmEmail_PreservesLegacyTokenAndDecodesV2Token(bool useV2Encoding)
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "testuser",
            Email = "test@example.com",
            EmailConfirmed = false,
        };
        const string identityToken = "CfDJ8D+/token/with+plus==";
        var requestToken = useV2Encoding
            ? $"v2.{WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(identityToken))}"
            : identityToken;
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager
            .Setup(x => x.ConfirmEmailAsync(user, identityToken))
            .ReturnsAsync(IdentityResult.Success);
        var refreshTokenService = new Mock<IRefreshTokenService>();
        refreshTokenService
            .Setup(x =>
                x.CreateUserRefreshTokenAsync(
                    user.Id,
                    "Email Confirmation Device",
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync(
                ApiResponse<RefreshTokenResultDto>.Success(
                    new RefreshTokenResultDto
                    {
                        AccessToken = "access-token",
                        RefreshToken = "refresh-token",
                    }
                )
            );
        var service = CreateService(userManager, refreshTokenService);

        var result = await service.ConfirmEmail(user.Id, requestToken);

        Assert.True(result.IsSuccessfull);
        userManager.Verify(x => x.ConfirmEmailAsync(user, identityToken), Times.Once);
    }
}
