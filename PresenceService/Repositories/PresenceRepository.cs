using System.Collections.Concurrent;
using PresenceService.Interfaces;
using PresenceService.Models;

namespace PresenceService.Repositories;

public class PresenceRepository : IPresenceRepository
{
    private readonly object _connectionGate = new();
    // Online presence: userId → connectionIds
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _userConnections = new();
    private readonly ConcurrentDictionary<string, string> _connectionUsers = new();

    // Clan subscriptions per connection: connectionId → clanIds
    private readonly ConcurrentDictionary<string, List<string>> _connectionClans = new();

    // DM conversation subscriptions per connection: connectionId → conversationIds
    private readonly ConcurrentDictionary<string, List<string>> _connectionConversations = new();

    // Friend presence watchers
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _userWatchers = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _connectionWatchedUsers = new();

    // Voice channel data: clanId → voiceChannelId → participants.
    // DM voice rooms (clanId == null) are bucketed under this sentinel key;
    // voiceChannelId ("dm-{conversationId}") is already globally unique so no collisions occur.
    private const string DmClanBucket = "__dm__";
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<UserInfo>>> _voicePresence = new();

    // Voice connection tracking for cleanup: connectionId → (ClanId, ChannelId, UserId)
    private readonly ConcurrentDictionary<string, (string? ClanId, string ChannelId, string UserId)> _voiceConnections = new();

    // ── Online presence ────────────────────────────────────────────────────────

    public Task<bool> AddUserConnection(string userId, string connectionId)
    {
        lock (_connectionGate)
        {
            var connections = _userConnections.GetOrAdd(userId, _ => new());
            var becameOnline = connections.IsEmpty;
            connections[connectionId] = 0;
            _connectionUsers[connectionId] = userId;
            return Task.FromResult(becameOnline);
        }
    }

    public Task<bool> RemoveUserConnection(string userId, string connectionId)
    {
        lock (_connectionGate)
        {
            _connectionUsers.TryRemove(connectionId, out _);
            if (!_userConnections.TryGetValue(userId, out var connections))
                return Task.FromResult(false);

            connections.TryRemove(connectionId, out _);
            if (!connections.IsEmpty) return Task.FromResult(false);
            var becameOffline = _userConnections.TryRemove(userId, out _);
            return Task.FromResult(becameOffline);
        }
    }

    public Task<bool> IsUserOnline(string userId) =>
        Task.FromResult(_userConnections.TryGetValue(userId, out var connections) && !connections.IsEmpty);

    public Task<List<string>> GetUserConnections(string userId) =>
        Task.FromResult(_userConnections.TryGetValue(userId, out var connections)
            ? connections.Keys.ToList()
            : new List<string>());

    // ── Clan subscriptions ─────────────────────────────────────────────────────

    public Task SetConnectionClans(string connectionId, List<string> clanIds)
    {
        _connectionClans[connectionId] = clanIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return Task.CompletedTask;
    }

    public Task AddConnectionClan(string connectionId, string clanId)
    {
        var clans = _connectionClans.GetOrAdd(connectionId, _ => new List<string>());
        lock (clans)
        {
            if (!clans.Contains(clanId, StringComparer.Ordinal)) clans.Add(clanId);
        }
        return Task.CompletedTask;
    }

    public Task RemoveConnectionClan(string connectionId, string clanId)
    {
        if (_connectionClans.TryGetValue(connectionId, out var clans))
        {
            lock (clans)
            {
                clans.RemoveAll(id => string.Equals(id, clanId, StringComparison.Ordinal));
            }
        }
        return Task.CompletedTask;
    }

    public Task<List<string>> GetConnectionClans(string connectionId)
    {
        _connectionClans.TryGetValue(connectionId, out var clans);
        if (clans == null) return Task.FromResult(new List<string>());
        lock (clans)
        {
            return Task.FromResult(new List<string>(clans));
        }
    }

    public Task RemoveConnectionClans(string connectionId)
    {
        _connectionClans.TryRemove(connectionId, out _);
        return Task.CompletedTask;
    }

    // ── DM conversation subscriptions ──────────────────────────────────────────

    public Task SetConnectionConversations(string connectionId, List<string> conversationIds)
    {
        _connectionConversations[connectionId] = conversationIds;
        return Task.CompletedTask;
    }

    public Task<List<string>> GetConnectionConversations(string connectionId)
    {
        _connectionConversations.TryGetValue(connectionId, out var conversations);
        return Task.FromResult(conversations ?? new List<string>());
    }

    public Task RemoveConnectionConversations(string connectionId)
    {
        _connectionConversations.TryRemove(connectionId, out _);
        return Task.CompletedTask;
    }

    // ── Friend presence subscriptions ─────────────────────────────────────────

    public Task SetConnectionWatchedUsers(string connectionId, List<string> userIds)
    {
        RemoveConnectionWatchedUsersCore(connectionId);
        var watched = _connectionWatchedUsers.GetOrAdd(connectionId, _ => new());
        foreach (var userId in userIds.Distinct(StringComparer.Ordinal))
        {
            watched[userId] = 0;
            _userWatchers.GetOrAdd(userId, _ => new())[connectionId] = 0;
        }
        return Task.CompletedTask;
    }

    public Task<List<string>> GetConnectionWatchedUsers(string connectionId) =>
        Task.FromResult(_connectionWatchedUsers.TryGetValue(connectionId, out var watched)
            ? watched.Keys.ToList()
            : new List<string>());

    public Task RemoveConnectionWatchedUsers(string connectionId)
    {
        RemoveConnectionWatchedUsersCore(connectionId);
        return Task.CompletedTask;
    }

    public Task<List<string>> GetWatchersOfUser(string userId) =>
        Task.FromResult(_userWatchers.TryGetValue(userId, out var watchers)
            ? watchers.Keys.ToList()
            : new List<string>());

    public Task RemoveWatchedUser(string connectionId, string userId)
    {
        if (_connectionWatchedUsers.TryGetValue(connectionId, out var watched))
        {
            watched.TryRemove(userId, out _);
            if (watched.IsEmpty) _connectionWatchedUsers.TryRemove(connectionId, out _);
        }
        if (_userWatchers.TryGetValue(userId, out var watchers))
        {
            watchers.TryRemove(connectionId, out _);
            if (watchers.IsEmpty) _userWatchers.TryRemove(userId, out _);
        }
        return Task.CompletedTask;
    }

    // ── Voice channel presence ─────────────────────────────────────────────────

    public Task JoinVoiceChannel(string connectionId, string userId, string userName, string? clanId, string voiceChannelId)
    {
        var bucket = string.IsNullOrEmpty(clanId) ? DmClanBucket : clanId;
        var channels = _voicePresence.GetOrAdd(bucket, _ => new ConcurrentDictionary<string, List<UserInfo>>());
        var participants = channels.GetOrAdd(voiceChannelId, _ => new List<UserInfo>());

        lock (participants)
        {
            participants.RemoveAll(u => u.UserId == userId);
            participants.Add(new UserInfo(userId, userName));
        }

        _voiceConnections[connectionId] = (clanId, voiceChannelId, userId);
        return Task.CompletedTask;
    }

    public Task<(string? ClanId, string ChannelId, string UserId)?> LeaveVoiceChannel(string connectionId)
    {
        if (!_voiceConnections.TryRemove(connectionId, out var info))
            return Task.FromResult<(string?, string, string)?>(null);

        RemoveVoiceParticipant(info.ClanId, info.ChannelId, info.UserId);
        return Task.FromResult<(string?, string, string)?>(info);
    }

    public Task<Dictionary<string, List<UserInfo>>> GetVoiceChannelParticipants(string clanId)
    {
        var result = new Dictionary<string, List<UserInfo>>();

        if (_voicePresence.TryGetValue(clanId, out var channels))
        {
            foreach (var (channelId, users) in channels)
            {
                lock (users)
                {
                    result[channelId] = new List<UserInfo>(users);
                }
            }
        }

        return Task.FromResult(result);
    }

public async Task DeleteVoiceChannel(string clanId, string channelId)
{
    // 1. Ses varlığı listesinden (Participants) kanalı tamamen uçur
    if (_voicePresence.TryGetValue(clanId, out var channels))
    {
        channels.TryRemove(channelId, out _);
        
        if (channels.IsEmpty)
            _voicePresence.TryRemove(clanId, out _);
    }

    // 2. Bağlantı takibi (Cleanup mapping) kısmından bu kanalda olan HERKESİ temizle
    // Bu kısım önemli, çünkü kullanıcı koptuğunda "zaten silinmiş bir kanaldan" ayrılmaya çalışmasın.
    var connectionsToRemove = _voiceConnections
        .Where(x => x.Value.ChannelId == channelId)
        .Select(x => x.Key)
        .ToList();

    foreach (var connId in connectionsToRemove)
    {
        _voiceConnections.TryRemove(connId, out _);
    }

    await Task.CompletedTask;
}

    public async Task DeleteClan(string clanId)
    {
        // 1. Ses varlığından (voice presence) klanı tüm kanallarıyla sil
        if (_voicePresence.TryRemove(clanId, out _))
        {
            // Bu klanda ses kanalında olan tüm bağlantıların takibini temizle
            var voiceConnsToRemove = _voiceConnections
                .Where(x => x.Value.ClanId == clanId)
                .Select(x => x.Key)
                .ToList();

            foreach (var connId in voiceConnsToRemove)
                _voiceConnections.TryRemove(connId, out _);
        }

        // 2. Clan subscription trackinginden bu klanı tüm bağlantılardan kaldır
        foreach (var (connId, clans) in _connectionClans)
        {
            lock (clans)
            {
                clans.Remove(clanId);
            }
        }

        await Task.CompletedTask;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void RemoveVoiceParticipant(string? clanId, string voiceChannelId, string userId)
    {
        var bucket = string.IsNullOrEmpty(clanId) ? DmClanBucket : clanId;
        if (!_voicePresence.TryGetValue(bucket, out var channels) ||
            !channels.TryGetValue(voiceChannelId, out var participants))
            return;

        lock (participants)
        {
            participants.RemoveAll(u => u.UserId == userId);
        }

        if (participants.Count == 0)
            channels.TryRemove(voiceChannelId, out _);

        if (channels.IsEmpty)
            _voicePresence.TryRemove(bucket, out _);
    }

    private void RemoveConnectionWatchedUsersCore(string connectionId)
    {
        if (!_connectionWatchedUsers.TryRemove(connectionId, out var watched)) return;
        foreach (var userId in watched.Keys)
        {
            if (_userWatchers.TryGetValue(userId, out var watchers))
            {
                watchers.TryRemove(connectionId, out _);
                if (watchers.IsEmpty) _userWatchers.TryRemove(userId, out _);
            }
        }
    }
}
