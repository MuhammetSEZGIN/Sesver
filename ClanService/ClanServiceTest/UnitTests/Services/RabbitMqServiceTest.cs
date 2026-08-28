using ClanService.Interfaces.Repositories;
using ClanService.Models;
using ClanService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Contracts;

namespace ClanServiceTest.UnitTests.Services;

public class RabbitMqServiceTest
{
    [Fact]
    public async Task ConsumeUserInformation_ExistingUser_UpdatesTrackedEntity()
    {
        var existing = new User
        {
            Id = "user-1",
            Username = "old-name",
            AvatarUrl = "old-avatar",
        };
        var repository = new Mock<IUserRepository>();
        repository.Setup(x => x.GetByIdAsync(existing.Id)).ReturnsAsync(existing);
        var service = CreateService(repository);
        var message = new UserUpdatedMessage
        {
            userId = existing.Id,
            userName = "new-name",
            AvatarUrl = "new-avatar",
        };

        await service.ConsumeUserInformation(message);

        Assert.Equal("new-name", existing.Username);
        Assert.Equal("new-avatar", existing.AvatarUrl);
        repository.Verify(x => x.UpdateAsync(existing), Times.Once);
        repository.Verify(x => x.AddAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeUserInformation_NewUser_AddsUser()
    {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(x => x.GetByIdAsync("user-1"))
            .ReturnsAsync((User?)null);
        var service = CreateService(repository);
        var message = new UserUpdatedMessage
        {
            userId = "user-1",
            userName = "new-name",
            AvatarUrl = "new-avatar",
        };

        await service.ConsumeUserInformation(message);

        repository.Verify(
            x => x.AddAsync(It.Is<User>(user =>
                user.Id == message.userId
                && user.Username == message.userName
                && user.AvatarUrl == message.AvatarUrl)),
            Times.Once);
    }

    [Fact]
    public async Task ConsumeUserDeleted_ExistingUser_DeletesUser()
    {
        var existing = new User { Id = "user-1", Username = "name" };
        var repository = new Mock<IUserRepository>();
        repository.Setup(x => x.GetByIdAsync(existing.Id)).ReturnsAsync(existing);
        var service = CreateService(repository);

        await service.ConsumeUserDeleted(new UserDeletedMessage { UserId = existing.Id });

        repository.Verify(x => x.DeleteAsync(existing), Times.Once);
    }

    [Fact]
    public async Task ConsumeUserDeleted_AbsentUser_IsIdempotent()
    {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(x => x.GetByIdAsync("missing"))
            .ReturnsAsync((User?)null);
        var service = CreateService(repository);

        await service.ConsumeUserDeleted(new UserDeletedMessage { UserId = "missing" });

        repository.Verify(x => x.DeleteAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeUserInformation_RepositoryFailure_IsPropagatedForRetry()
    {
        var repository = new Mock<IUserRepository>();
        repository
            .Setup(x => x.GetByIdAsync("user-1"))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var service = CreateService(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConsumeUserInformation(new UserUpdatedMessage { userId = "user-1" }));

        Assert.Equal("database unavailable", exception.Message);
    }

    private static RabbitMqService CreateService(Mock<IUserRepository> repository) =>
        new(repository.Object, Mock.Of<ILogger<RabbitMqService>>());
}
