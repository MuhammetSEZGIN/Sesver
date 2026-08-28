using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace IdentityServiceTests.UnitTests.Services;

public class UserServiceTests
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

    private static UserService CreateService(
        Mock<UserManager<ApplicationUser>> userManager,
        Mock<IEmailService> emailService,
        Mock<IRefreshTokenService>? refreshTokenService = null,
        Mock<IIdentityProducer>? identityProducer = null
    ) =>
        new UserService(
            userManager.Object,
            emailService.Object,
            Mock.Of<ILogger<UserService>>(),
            new ConfigurationBuilder().Build(),
            (refreshTokenService ?? new Mock<IRefreshTokenService>()).Object,
            (identityProducer ?? new Mock<IIdentityProducer>()).Object
        );

    [Fact]
    public async Task ChangeEmailAsync_ChangesAddressAndSendsConfirmation()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "old@example.com",
            EmailConfirmed = true,
        };
        var model = new ChangeEmailRequestDto { Email = "new@example.com" };
        const string confirmationUrl = "https://api.example.com/identity/confirm-email";
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager
            .Setup(x => x.FindByEmailAsync(model.Email))
            .ReturnsAsync((ApplicationUser)null);
        userManager
            .Setup(x => x.SetEmailAsync(user, model.Email))
            .Callback<ApplicationUser, string>(
                (target, email) =>
                {
                    target.Email = email;
                    target.EmailConfirmed = false;
                }
            )
            .ReturnsAsync(IdentityResult.Success);
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(x => x.SendEmailConfirmationAsync(user.Id, confirmationUrl))
            .ReturnsAsync(ApiResponse<object>.Success("sent"));
        var service = CreateService(userManager, emailService);

        var result = await service.ChangeEmailAsync(user.Id, model, confirmationUrl);

        Assert.True(result.IsSuccessfull);
        Assert.Equal(model.Email, user.Email);
        Assert.False(user.EmailConfirmed);
        userManager.Verify(x => x.SetEmailAsync(user, model.Email), Times.Once);
        emailService.Verify(
            x => x.SendEmailConfirmationAsync(user.Id, confirmationUrl),
            Times.Once
        );
    }

    [Fact]
    public async Task ChangeEmailAsync_DuplicateAddress_DoesNotSendConfirmation()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "old@example.com",
            EmailConfirmed = true,
        };
        var model = new ChangeEmailRequestDto { Email = "taken@example.com" };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager
            .Setup(x => x.FindByEmailAsync(model.Email))
            .ReturnsAsync(new ApplicationUser { Id = "another-user", Email = model.Email });
        var emailService = new Mock<IEmailService>();
        var service = CreateService(userManager, emailService);

        var result = await service.ChangeEmailAsync(
            user.Id,
            model,
            "https://api.example.com/identity/confirm-email"
        );

        Assert.False(result.IsSuccessfull);
        Assert.Equal(400, result.StatusCode);
        userManager.Verify(
            x => x.SetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never
        );
        emailService.Verify(
            x => x.SendEmailConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ChangeEmailAsync_SameUnconfirmedAddress_ResendsConfirmation()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "same@example.com",
            EmailConfirmed = false,
        };
        var model = new ChangeEmailRequestDto { Email = "SAME@example.com" };
        const string confirmationUrl = "https://api.example.com/identity/confirm-email";
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        var emailService = new Mock<IEmailService>();
        emailService
            .Setup(x => x.SendEmailConfirmationAsync(user.Id, confirmationUrl))
            .ReturnsAsync(ApiResponse<object>.Success("sent"));
        var service = CreateService(userManager, emailService);

        var result = await service.ChangeEmailAsync(user.Id, model, confirmationUrl);

        Assert.True(result.IsSuccessfull);
        userManager.Verify(
            x => x.SetEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never
        );
        emailService.Verify(
            x => x.SendEmailConfirmationAsync(user.Id, confirmationUrl),
            Times.Once
        );
    }

    [Fact]
    public async Task UpdateUserAsync_PersistsProfileBackgroundUrl()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "testuser" };
        var model = new UpdateUserModel
        {
            UserName = "testuser",
            AvatarUrl = "https://example.com/avatar.png",
            Bio = "hello",
            ProfileBackgroundUrl = "https://example.com/background.gif",
        };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        var service = CreateService(userManager, new Mock<IEmailService>());

        var result = await service.UpdateUserAsync(user.Id, model);

        Assert.True(result.Succeeded);
        Assert.Equal("https://example.com/background.gif", user.ProfileBackgroundUrl);
    }

    [Fact]
    public async Task UpdateUserAsync_Success_PublishesUpdatedUserInformation()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "old-name",
            AvatarUrl = "https://example.com/old.png",
        };
        var model = new UpdateUserModel
        {
            UserName = "new-name",
            AvatarUrl = "https://example.com/new.png",
        };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        var identityProducer = new Mock<IIdentityProducer>();
        var service = CreateService(
            userManager,
            new Mock<IEmailService>(),
            identityProducer: identityProducer
        );

        var result = await service.UpdateUserAsync(user.Id, model);

        Assert.True(result.Succeeded);
        identityProducer.Verify(
            producer => producer.PublishUserUpdatedMessageAsync(
                model.UserName,
                model.AvatarUrl,
                user.Id),
            Times.Once);
    }

    [Fact]
    public async Task UpdateUserAsync_Failure_DoesNotPublishUpdatedUserInformation()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "old-name" };
        var model = new UpdateUserModel { UserName = "new-name" };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager
            .Setup(x => x.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError()));
        var identityProducer = new Mock<IIdentityProducer>();
        var service = CreateService(
            userManager,
            new Mock<IEmailService>(),
            identityProducer: identityProducer
        );

        var result = await service.UpdateUserAsync(user.Id, model);

        Assert.False(result.Succeeded);
        identityProducer.Verify(
            producer => producer.PublishUserUpdatedMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteUserAsync_Success_PublishesUserDeletedMessage()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "testuser" };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager.Setup(x => x.DeleteAsync(user)).ReturnsAsync(IdentityResult.Success);
        var identityProducer = new Mock<IIdentityProducer>();
        var service = CreateService(
            userManager,
            new Mock<IEmailService>(),
            identityProducer: identityProducer
        );

        var result = await service.DeleteUserAsync(user.Id);

        Assert.True(result.Succeeded);
        identityProducer.Verify(
            producer => producer.PublishUserDeletedMessageAsync(user.Id),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateUserAsync_ClearedProfileBackgroundUrl_StoresNull(string? cleared)
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "testuser",
            ProfileBackgroundUrl = "https://example.com/old.png",
        };
        var model = new UpdateUserModel
        {
            UserName = "testuser",
            ProfileBackgroundUrl = cleared,
        };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager.Setup(x => x.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        var service = CreateService(userManager, new Mock<IEmailService>());

        var result = await service.UpdateUserAsync(user.Id, model);

        Assert.True(result.Succeeded);
        Assert.Null(user.ProfileBackgroundUrl);
    }

    [Fact]
    public async Task GetMeAsync_ReturnsProfileBackgroundUrl()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            UserName = "testuser",
            Email = "test@example.com",
            Bio = "hello",
            AvatarUrl = "https://example.com/avatar.png",
            ProfileBackgroundUrl = "https://example.com/background.gif",
        };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        var service = CreateService(userManager, new Mock<IEmailService>());

        var result = await service.GetMeAsync(user.Id);

        Assert.Equal("https://example.com/background.gif", result.ProfileBackgroundUrl);
    }

    [Fact]
    public async Task GetUserProfileAsync_ReturnsPublicFieldsOnly()
    {
        var user = new ApplicationUser
        {
            Id = "user-2",
            UserName = "otheruser",
            Email = "other@example.com",
            Bio = "hello",
            AvatarUrl = "https://example.com/avatar.png",
            ProfileBackgroundUrl = "https://example.com/background.gif",
        };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        var service = CreateService(userManager, new Mock<IEmailService>());

        var result = await service.GetUserProfileAsync(user.Id);

        Assert.True(result.IsSuccessfull);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(user.Id, result.Data.Id);
        Assert.Equal("otheruser", result.Data.UserName);
        Assert.Equal("hello", result.Data.Bio);
        Assert.Equal("https://example.com/avatar.png", result.Data.AvatarUrl);
        Assert.Equal("https://example.com/background.gif", result.Data.ProfileBackgroundUrl);
        Assert.DoesNotContain(
            typeof(UserProfileDto).GetProperties(),
            property => property.Name == "Email"
        );
    }

    [Fact]
    public async Task GetUserProfileAsync_UnknownUser_ReturnsNotFound()
    {
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync("missing")).ReturnsAsync((ApplicationUser)null);
        var service = CreateService(userManager, new Mock<IEmailService>());

        var result = await service.GetUserProfileAsync("missing");

        Assert.False(result.IsSuccessfull);
        Assert.Equal(404, result.StatusCode);
        Assert.Null(result.Data);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetUserProfileAsync_BlankId_ReturnsNotFoundWithoutLookup(string? userId)
    {
        var userManager = BuildUserManager();
        var service = CreateService(userManager, new Mock<IEmailService>());

        var result = await service.GetUserProfileAsync(userId);

        Assert.False(result.IsSuccessfull);
        Assert.Equal(404, result.StatusCode);
        userManager.Verify(x => x.FindByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ChangePasswordAsync_Success_InvalidatesEverySession()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "testuser" };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager
            .Setup(x => x.ChangePasswordAsync(user, "OldPass@123", "NewPass@123"))
            .ReturnsAsync(IdentityResult.Success);
        var refreshTokenService = new Mock<IRefreshTokenService>();
        refreshTokenService
            .Setup(x => x.InvalidateAllUserSessionsAsync(user.Id))
            .ReturnsAsync(true);
        var service = CreateService(
            userManager,
            new Mock<IEmailService>(),
            refreshTokenService
        );

        var result = await service.ChangePasswordAsync(
            user.Id,
            "OldPass@123",
            "NewPass@123"
        );

        Assert.True(result.IsSuccessfull);
        refreshTokenService.Verify(
            x => x.InvalidateAllUserSessionsAsync(user.Id),
            Times.Once
        );
    }

    [Fact]
    public async Task ChangePasswordAsync_InvalidPassword_DoesNotInvalidateSessions()
    {
        var user = new ApplicationUser { Id = "user-1", UserName = "testuser" };
        var userManager = BuildUserManager();
        userManager.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager
            .Setup(x => x.ChangePasswordAsync(user, "WrongPass@123", "NewPass@123"))
            .ReturnsAsync(
                IdentityResult.Failed(
                    new IdentityError { Description = "Incorrect password" }
                )
            );
        var refreshTokenService = new Mock<IRefreshTokenService>();
        var service = CreateService(
            userManager,
            new Mock<IEmailService>(),
            refreshTokenService
        );

        var result = await service.ChangePasswordAsync(
            user.Id,
            "WrongPass@123",
            "NewPass@123"
        );

        Assert.False(result.IsSuccessfull);
        refreshTokenService.Verify(
            x => x.InvalidateAllUserSessionsAsync(It.IsAny<string>()),
            Times.Never
        );
    }
}
