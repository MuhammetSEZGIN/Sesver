using System.Collections.Concurrent;
using PresenceService.Interfaces;
using PresenceService.Models;

namespace PresenceService.Repositories;

public class PresenceRepository : IPresenceRepository
{
    // Online presence: userId → connectionId
    private readonly ConcurrentDictionary<string, string> _userConnections = new();

    // Clan subscriptions per connection: connectionId → clanIds
    private readonly ConcurrentDictionary<string, List<string>> _connectionClans = new();

    // DM conversation subscriptions per connection: connectionId → conversationIds
    private readonly ConcurrentDictionary<string, List<string>> _connectionConversations = new();

    // Voice channel data: clanId → voiceChannelId → participants.
    // DM voice rooms (clanId == null) are bucketed under this sentinel key;
    // voiceChannelId ("dm-{conversationId}") is already globally unique so no collisions occur.
    private const string DmClanBucket = "__dm__";
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<UserInfo>>> _voicePresence = new();

    // Voice connection tracking for cleanup: connectionId → (ClanId, ChannelId, UserId)
    private readonly ConcurrentDictionary<string, (string? ClanId, string ChannelId, string UserId)> _voiceConnections = new();

    // ── Online presence ────────────────────────────────────────────────────────

    public Task SetUserOnline(string userId, string connectionId)
    {
        _userConnections[userId] = connectionId;
        return Task.CompletedTask;
    }

    public Task<string?> SetUserOffline(string userId)
    {
        _userConnections.TryRemove(userId, out var connectionId);
        return Task.FromResult<string?>(connectionId);
    }

    public Task<bool> IsUserOnline(string userId) =>
        Task.FromResult(_userConnections.ContainsKey(userId));

    // ── Clan subscriptions ─────────────────────────────────────────────────────

    public Task SetConnectionClans(string connectionId, List<string> clanIds)
    {
        _connectionClans[connectionId] = clanIds;
        return Task.CompletedTask;
    }

    public Task<List<string>> GetConnectionClans(string connectionId)
    {
        _connectionClans.TryGetValue(connectionId, out var clans);
        return Task.FromResult(clans ?? new List<string>());
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
            _voicePresence.TryRemove(clanId, out _);
    }
}

