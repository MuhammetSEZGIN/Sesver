using PresenceService.Models;
using PresenceService.Repositories;

namespace FeatureTests;

public class CallRepositoryTests
{
    [Fact]
    public void CallLifecycleEnforcesRolesAndBusyState()
    {
        var repository = new CallRepository();
        var created = repository.TryCreate("conversation", "caller", "callee", "caller-connection");

        Assert.True(created.Succeeded);
        Assert.Equal("caller-connection", created.Call!.CallerConnectionId);
        Assert.False(repository.TryCreate("other", "caller", "third", "other-connection").Succeeded);
        Assert.False(repository.Cancel(created.Call!.CallId, "callee").Succeeded);
        var accepted = repository.Accept(created.Call.CallId, "callee", "callee-connection");
        Assert.True(accepted.Succeeded);
        Assert.Equal("callee-connection", accepted.Call!.CalleeConnectionId);
        Assert.True(repository.End(created.Call.CallId, "caller").Succeeded);
        Assert.True(repository.TryCreate("other", "caller", "third", "caller-connection").Succeeded);
    }

    [Fact]
    public void ExpiredRingingCallIsReleasedOnce()
    {
        var repository = new CallRepository();
        repository.TryCreate("conversation", "caller", "callee", "caller-connection");

        var expired = repository.TimeoutExpired(DateTime.UtcNow.AddMinutes(1));

        Assert.Single(expired);
        Assert.Equal(CallStatus.TimedOut, expired[0].Status);
        Assert.Empty(repository.TimeoutExpired(DateTime.UtcNow.AddMinutes(1)));
    }
}
