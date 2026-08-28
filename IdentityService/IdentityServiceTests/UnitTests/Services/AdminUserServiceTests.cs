using IdentityService.Data;
using IdentityService.Interfaces;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace IdentityServiceTests.UnitTests.Services;

public class AdminUserServiceTests
{
    [Fact]
    public async Task GetUsersAsync_ReturnsStablePagedResult()
    {
        await using var context = BuildContext();
        context.Users.AddRange(
            new ApplicationUser
            {
                Id = "user-3",
                UserName = "charlie",
                NormalizedUserName = "CHARLIE",
                Email = "charlie@example.com",
                NormalizedEmail = "CHARLIE@EXAMPLE.COM"
            },
            new ApplicationUser
            {
                Id = "user-1",
                UserName = "alice",
                NormalizedUserName = "ALICE",
                Email = "alice@example.com",
                NormalizedEmail = "ALICE@EXAMPLE.COM"
            },
            new ApplicationUser
            {
                Id = "user-2",
                UserName = "bob",
                NormalizedUserName = "BOB",
                Email = "bob@example.com",
                NormalizedEmail = "BOB@EXAMPLE.COM"
            });
        await context.SaveChangesAsync();
        var userManager = BuildUserManager();
        userManager.SetupGet(manager => manager.Users).Returns(context.Users);
        var service = CreateService(userManager, context);

        var result = await service.GetUsersAsync(null, page: 2, limit: 2);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.Limit);
        var item = Assert.Single(result.Items);
        Assert.Equal("charlie", item.UserName);
    }

    [Fact]
    public async Task GetUsersAsync_FiltersByNormalizedNameOrEmail()
    {
        await using var context = BuildContext();
        context.Users.AddRange(
            new ApplicationUser
            {
                Id = "user-1",
                UserName = "alice",
                NormalizedUserName = "ALICE",
                Email = "alice@example.com",
                NormalizedEmail = "ALICE@EXAMPLE.COM"
            },
            new ApplicationUser
            {
                Id = "user-2",
                UserName = "bob",
                NormalizedUserName = "BOB",
                Email = "support@company.test",
                NormalizedEmail = "SUPPORT@COMPANY.TEST"
            });
        await context.SaveChangesAsync();
        var userManager = BuildUserManager();
        userManager.SetupGet(manager => manager.Users).Returns(context.Users);
        var service = CreateService(userManager, context);

        var byName = await service.GetUsersAsync("ALi", page: 1, limit: 20);
        var byEmail = await service.GetUsersAsync("company", page: 1, limit: 20);

        Assert.Equal("alice", Assert.Single(byName.Items).UserName);
        Assert.Equal("bob", Assert.Single(byEmail.Items).UserName);
    }

    [Fact]
    public async Task DeleteUserAsync_RejectsSelfDeletion()
    {
        await using var context = BuildContext();
        var userManager = BuildUserManager();
        var service = CreateService(userManager, context);

        var result = await service.DeleteUserAsync("admin-1", "admin-1");

        Assert.False(result.IsSuccessfull);
        Assert.Equal(400, result.StatusCode);
        userManager.Verify(
            manager => manager.FindByIdAsync(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteUserAsync_ReturnsNotFoundForMissingUser()
    {
        await using var context = BuildContext();
        var userManager = BuildUserManager();
        userManager
            .Setup(manager => manager.FindByIdAsync("missing"))
            .ReturnsAsync((ApplicationUser)null);
        var service = CreateService(userManager, context);

        var result = await service.DeleteUserAsync("admin-1", "missing");

        Assert.False(result.IsSuccessfull);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteUserAsync_AbortsWhenSessionsCannotBeInvalidated()
    {
        await using var context = BuildContext();
        var target = new ApplicationUser { Id = "target", UserName = "target" };
        var userManager = BuildUserManager();
        userManager.Setup(manager => manager.FindByIdAsync(target.Id)).ReturnsAsync(target);
        var refreshTokenService = new Mock<IRefreshTokenService>();
        refreshTokenService
            .Setup(service => service.InvalidateAllUserSessionsAsync(target.Id))
            .ReturnsAsync(false);
        var service = CreateService(userManager, context, refreshTokenService);

        var result = await service.DeleteUserAsync("admin-1", target.Id);

        Assert.False(result.IsSuccessfull);
        Assert.Equal(503, result.StatusCode);
        userManager.Verify(
            manager => manager.DeleteAsync(It.IsAny<ApplicationUser>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteUserAsync_RemovesFriendshipsInBothDirectionsBeforeUser()
    {
        await using var context = BuildContext();
        var target = new ApplicationUser { Id = "target", UserName = "target" };
        var firstFriend = new ApplicationUser { Id = "friend-1", UserName = "friend1" };
        var secondFriend = new ApplicationUser { Id = "friend-2", UserName = "friend2" };
        context.Users.AddRange(target, firstFriend, secondFriend);
        context.Friendships.AddRange(
            new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = target.Id,
                AddresseeId = firstFriend.Id
            },
            new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = secondFriend.Id,
                AddresseeId = target.Id
            },
            new Friendship
            {
                Id = Guid.NewGuid(),
                RequesterId = firstFriend.Id,
                AddresseeId = secondFriend.Id
            });
        await context.SaveChangesAsync();
        var userManager = BuildUserManager();
        userManager.Setup(manager => manager.FindByIdAsync(target.Id)).ReturnsAsync(target);
        userManager
            .Setup(manager => manager.DeleteAsync(target))
            .Returns(async () =>
            {
                context.Users.Remove(target);
                await context.SaveChangesAsync();
                return IdentityResult.Success;
            });
        var identityProducer = new Mock<IIdentityProducer>();
        var service = CreateService(
            userManager,
            context,
            identityProducer: identityProducer);

        var result = await service.DeleteUserAsync("admin-1", target.Id);

        Assert.True(result.IsSuccessfull);
        Assert.Equal(204, result.StatusCode);
        Assert.False(await context.Users.AnyAsync(user => user.Id == target.Id));
        Assert.False(await context.Friendships.AnyAsync(friendship =>
            friendship.RequesterId == target.Id || friendship.AddresseeId == target.Id));
        Assert.Equal(1, await context.Friendships.CountAsync());
        identityProducer.Verify(
            producer => producer.PublishUserDeletedMessageAsync(target.Id),
            Times.Once);
    }

    [Fact]
    public async Task DeleteUserAsync_MapsIdentityFailureToBadRequest()
    {
        await using var context = BuildContext();
        var target = new ApplicationUser { Id = "target", UserName = "target" };
        var userManager = BuildUserManager();
        userManager.Setup(manager => manager.FindByIdAsync(target.Id)).ReturnsAsync(target);
        userManager
            .Setup(manager => manager.DeleteAsync(target))
            .ReturnsAsync(
                IdentityResult.Failed(
                    new IdentityError { Description = "Delete failed" }));
        var service = CreateService(userManager, context);

        var result = await service.DeleteUserAsync("admin-1", target.Id);

        Assert.False(result.IsSuccessfull);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Delete failed", result.Errors);
    }

    private static IdentityDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options);
    }

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
            null);
    }

    private static AdminUserService CreateService(
        Mock<UserManager<ApplicationUser>> userManager,
        IdentityDbContext context,
        Mock<IRefreshTokenService>? refreshTokenService = null,
        Mock<IIdentityProducer>? identityProducer = null)
    {
        if (refreshTokenService == null)
        {
            refreshTokenService = new Mock<IRefreshTokenService>();
            refreshTokenService
                .Setup(service => service.InvalidateAllUserSessionsAsync(It.IsAny<string>()))
                .ReturnsAsync(true);
        }

        return new AdminUserService(
            userManager.Object,
            context,
            refreshTokenService.Object,
            (identityProducer ?? new Mock<IIdentityProducer>()).Object,
            Mock.Of<ILogger<AdminUserService>>());
    }
}
