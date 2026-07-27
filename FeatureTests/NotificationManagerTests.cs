using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NotificationService.Data;
using NotificationService.Hubs;
using NotificationService.Services;
using Shared.Contracts;

namespace FeatureTests;

public class NotificationManagerTests
{
    [Fact]
    public async Task DuplicateEventIsStoredOnlyOnceAndQueriesAreUserScoped()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NotificationDbContext(options);
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.User(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var manager = new NotificationManager(db, hub.Object, NullLogger<NotificationManager>.Instance);
        var message = new NotificationRequestedMessage
        {
            EventId = Guid.NewGuid(), UserId = "owner", Type = NotificationType.MissedCall,
            Title = "Missed call", Body = "Body", CreatedAt = DateTime.UtcNow,
        };

        Assert.True(await manager.CreateAsync(message, CancellationToken.None));
        Assert.False(await manager.CreateAsync(message, CancellationToken.None));
        Assert.Equal(1, await manager.GetUnreadCountAsync("owner", CancellationToken.None));
        Assert.Equal(0, await manager.GetUnreadCountAsync("other", CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAllRemovesOnlyRequestingUsersNotifications()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NotificationDbContext(options);
        var proxy = new Mock<IClientProxy>();
        proxy.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.User(It.IsAny<string>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<NotificationHub>>();
        hub.SetupGet(x => x.Clients).Returns(clients.Object);
        var manager = new NotificationManager(db, hub.Object, NullLogger<NotificationManager>.Instance);

        await manager.CreateAsync(CreateMessage("owner"), CancellationToken.None);
        await manager.CreateAsync(CreateMessage("owner"), CancellationToken.None);
        await manager.CreateAsync(CreateMessage("other"), CancellationToken.None);

        Assert.Equal(2, await manager.DeleteAllAsync("owner", CancellationToken.None));
        Assert.Empty((await manager.GetAsync("owner", false, 1, 20, CancellationToken.None)).Items);
        Assert.Single((await manager.GetAsync("other", false, 1, 20, CancellationToken.None)).Items);
        proxy.Verify(x => x.SendCoreAsync(
            "NotificationsCleared",
            It.IsAny<object?[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static NotificationRequestedMessage CreateMessage(string userId) => new()
    {
        EventId = Guid.NewGuid(),
        UserId = userId,
        Type = NotificationType.DirectMessageReceived,
        Title = "New message",
        Body = "Body",
        CreatedAt = DateTime.UtcNow,
    };
}
