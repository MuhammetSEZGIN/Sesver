using System.Collections.Concurrent;
using MessageService.Interfaces.Services;

namespace MessageService.Services;

public class MessageConnectionTracker : IMessageConnectionTracker
{
    private readonly ConcurrentDictionary<(string UserId, string ChannelId), ConcurrentDictionary<string, byte>> _channelConnections = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<(string UserId, string ChannelId), byte>> _connectionChannels = new();

    public void JoinChannel(string userId, string connectionId, string channelId)
    {
        _channelConnections.GetOrAdd((userId, channelId), _ => new())[connectionId] = 0;
        _connectionChannels.GetOrAdd(connectionId, _ => new())[(userId, channelId)] = 0;
    }

    public void LeaveChannel(string userId, string connectionId, string channelId) =>
        RemoveMembership(connectionId, (userId, channelId));

    public void RemoveConnection(string connectionId)
    {
        if (!_connectionChannels.TryRemove(connectionId, out var memberships)) return;

        foreach (var membership in memberships.Keys)
        {
            if (_channelConnections.TryGetValue(membership, out var connections))
            {
                connections.TryRemove(connectionId, out _);
                if (connections.IsEmpty) _channelConnections.TryRemove(membership, out _);
            }
        }
    }

    public bool IsUserInChannel(string userId, string channelId) =>
        _channelConnections.TryGetValue((userId, channelId), out var connections) && !connections.IsEmpty;

    private void RemoveMembership(string connectionId, (string UserId, string ChannelId) membership)
    {
        if (_channelConnections.TryGetValue(membership, out var connections))
        {
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty) _channelConnections.TryRemove(membership, out _);
        }

        if (_connectionChannels.TryGetValue(connectionId, out var memberships))
        {
            memberships.TryRemove(membership, out _);
            if (memberships.IsEmpty) _connectionChannels.TryRemove(connectionId, out _);
        }
    }
}
