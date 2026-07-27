using PresenceService.Repositories;

namespace FeatureTests;

public class PresenceRepositoryTests
{
    [Fact]
    public async Task MultipleConnectionsOnlyTransitionAtEdges()
    {
        var repository = new PresenceRepository();

        Assert.True(await repository.AddUserConnection("user", "one"));
        Assert.False(await repository.AddUserConnection("user", "two"));
        Assert.False(await repository.RemoveUserConnection("user", "one"));
        Assert.True(await repository.IsUserOnline("user"));
        Assert.True(await repository.RemoveUserConnection("user", "two"));
        Assert.False(await repository.IsUserOnline("user"));
    }

    [Fact]
    public async Task ReplacingWatchedUsersCleansReverseIndex()
    {
        var repository = new PresenceRepository();
        await repository.SetConnectionWatchedUsers("connection", ["a", "b"]);
        await repository.SetConnectionWatchedUsers("connection", ["b"]);

        Assert.Empty(await repository.GetWatchersOfUser("a"));
        Assert.Equal(["connection"], await repository.GetWatchersOfUser("b"));
    }

    [Fact]
    public async Task ClanSubscriptionCanBeAddedAndRemovedForExistingConnection()
    {
        var repository = new PresenceRepository();
        await repository.SetConnectionClans("connection", ["one"]);

        await repository.AddConnectionClan("connection", "two");
        await repository.AddConnectionClan("connection", "two");
        Assert.Equal(["one", "two"], await repository.GetConnectionClans("connection"));

        await repository.RemoveConnectionClan("connection", "one");
        Assert.Equal(["two"], await repository.GetConnectionClans("connection"));
    }
}
