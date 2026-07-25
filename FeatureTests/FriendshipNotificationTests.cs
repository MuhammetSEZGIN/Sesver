using IdentityService.Data;
using IdentityService.Interfaces;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Contracts;

namespace FeatureTests;

public class FriendshipNotificationTests
{
    [Fact]
    public async Task AcceptPublishesNotificationAndRelationshipEvents()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new IdentityDbContext(options);
        var friendship = new Friendship
        {
            Id = Guid.NewGuid(), RequesterId = "requester", AddresseeId = "addressee",
            Status = FriendshipStatus.Pending,
        };
        db.Friendships.Add(friendship);
        await db.SaveChangesAsync();

        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        var producer = new Mock<IIdentityProducer>();
        var service = new FriendshipService(
            db, userManager.Object, NullLogger<FriendshipService>.Instance, producer.Object);

        var result = await service.AcceptRequestAsync("addressee", friendship.Id);

        Assert.True(result.Succeeded);
        producer.Verify(x => x.PublishNotificationAsync(It.Is<NotificationRequestedMessage>(m =>
            m.UserId == "requester" && m.Type == NotificationType.FriendRequestAccepted)), Times.Once);
        producer.Verify(x => x.PublishFriendshipRelationshipChangedAsync(
            It.Is<FriendshipRelationshipChangedMessage>(m =>
                m.Status == FriendshipRelationshipStatus.Accepted)), Times.Once);
    }
}
