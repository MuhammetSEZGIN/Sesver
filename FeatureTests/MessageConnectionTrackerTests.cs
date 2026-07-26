using MessageService.Services;

namespace FeatureTests;

public class MessageConnectionTrackerTests
{
    [Fact]
    public void TracksExactChannelAcrossConnections()
    {
        var tracker = new MessageConnectionTracker();
        tracker.JoinChannel("user", "one", "dm-a");
        tracker.JoinChannel("user", "two", "dm-b");

        Assert.True(tracker.IsUserInChannel("user", "dm-a"));
        Assert.True(tracker.IsConnectionInChannel("user", "one", "dm-a"));
        Assert.False(tracker.IsConnectionInChannel("user", "one", "dm-b"));
        tracker.RemoveConnection("one");
        Assert.False(tracker.IsUserInChannel("user", "dm-a"));
        Assert.False(tracker.IsConnectionInChannel("user", "one", "dm-a"));
        Assert.True(tracker.IsUserInChannel("user", "dm-b"));
    }
}
