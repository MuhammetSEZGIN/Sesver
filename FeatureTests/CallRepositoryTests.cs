using PresenceService.Models;
using PresenceService.Repositories;

namespace FeatureTests;

public class CallRepositoryTests
{
    [Fact]
    public void CallLifecycleEnforcesRolesAndBusyState()
    {
        var repository = new CallRepository();
        var created = repository.TryCreate("conversation", "caller", "callee");

        Assert.True(created.Succeeded);
        Assert.False(repository.TryCreate("other", "caller", "third").Succeeded);
        Assert.False(repository.Cancel(created.Call!.CallId, "callee").Succeeded);
        Assert.True(repository.Accept(created.Call.CallId, "callee").Succeeded);
        Assert.True(repository.End(created.Call.CallId, "caller").Succeeded);
        Assert.True(repository.TryCreate("other", "caller", "third").Succeeded);
    }

    [Fact]
    public void ExpiredRingingCallIsReleasedOnce()
    {
        var repository = new CallRepository();
        repository.TryCreate("conversation", "caller", "callee");

        var expired = repository.TimeoutExpired(DateTime.UtcNow.AddMinutes(1));

        Assert.Single(expired);
        Assert.Equal(CallStatus.TimedOut, expired[0].Status);
        Assert.Empty(repository.TimeoutExpired(DateTime.UtcNow.AddMinutes(1)));
    }
}
