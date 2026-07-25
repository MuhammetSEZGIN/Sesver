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
        Mock<IEmailService> emailService
    ) =>
        new UserService(
            userManager.Object,
            emailService.Object,
            Mock.Of<ILogger<UserService>>(),
            new ConfigurationBuilder().Build()
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
}
