using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PresenceService.Interfaces;

namespace PresenceService.Hubs;

[Authorize(AuthenticationSchemes = "Bearer")]
public class PresenceHub : Hub
{
    private const string DmVoiceRoomPrefix = "dm-";

    private readonly IPresenceRepository _repository;
    private readonly ILogger<PresenceHub> _logger;

    public PresenceHub(IPresenceRepository repository, ILogger<PresenceHub> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// DM ses odaları için yayın grubu "conversation_{conversationId}", klan ses
    /// odaları için "clan_{clanId}" kullanılır. voiceChannelId "dm-" ile başlıyorsa
    /// (clanId null demektir) conversationId ondan türetilir.
    /// </summary>
    private static string VoiceBroadcastGroup(string? clanId, string voiceChannelId)
    {
        if (string.IsNullOrEmpty(clanId) && voiceChannelId != null && voiceChannelId.StartsWith(DmVoiceRoomPrefix))
        {
            var conversationId = voiceChannelId[DmVoiceRoomPrefix.Length..];
            return $"conversation_{conversationId}";
        }
        return $"clan_{clanId}";
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await _repository.SetUserOnline(userId, Context.ConnectionId);
            _logger.LogInformation("User {UserId} connected", userId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            await _repository.SetUserOffline(userId);

            // Clean up voice channel if the user was in one
            var voiceInfo = await _repository.LeaveVoiceChannel(Context.ConnectionId);
            if (voiceInfo.HasValue)
            {
                var (clanId, channelId, uid) = voiceInfo.Value;
                _logger.LogInformation(
                    "Connection dropped — removing user {UserId} from voice channel {ChannelId} in clan {ClanId}",
                    uid, channelId, clanId ?? "(dm)");

                await Clients.Group(VoiceBroadcastGroup(clanId, channelId)).SendAsync("UserLeftVoice", new
                {
                    clanId,
                    voiceChannelId = channelId,
                    userId = uid
                });
            }

            // Notify subscribed clans that this user is offline
            var clanIds = await _repository.GetConnectionClans(Context.ConnectionId);
            foreach (var clanId in clanIds)
            {
                await Clients.Group($"clan_{clanId}").SendAsync("UserOffline", userId);
            }

            await _repository.RemoveConnectionClans(Context.ConnectionId);
            await _repository.RemoveConnectionConversations(Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    // ── Online presence ────────────────────────────────────────────────────────

    /// <summary>
    /// Client calls this after connecting to subscribe to clan presence events.
    /// The server adds the connection to each clan group and notifies members.
    /// </summary>
    public async Task SubscribeToClans(List<string> clanIds)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId) || clanIds == null || clanIds.Count == 0) return;

        foreach (var clanId in clanIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"clan_{clanId}");

        await _repository.SetConnectionClans(Context.ConnectionId, clanIds);

        // Notify each clan that this user is now online
        foreach (var clanId in clanIds)
            await Clients.Group($"clan_{clanId}").SendAsync("UserOnline", userId);
    }

    /// <summary>
    /// Client calls this after connecting to subscribe to DM conversation presence events
    /// (e.g. "is the other participant currently in the DM voice room").
    /// </summary>
    public async Task SubscribeToConversations(List<string> conversationIds)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId) || conversationIds == null || conversationIds.Count == 0) return;

        foreach (var conversationId in conversationIds)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"conversation_{conversationId}");

        await _repository.SetConnectionConversations(Context.ConnectionId, conversationIds);
    }

    /// <summary>
    /// Returns which of the given userIds are currently online.
    /// </summary>
    public async Task GetOnlineUsers(List<string> userIds)
    {
        var onlineUsers = new List<string>();
        foreach (var uid in userIds)
        {
            if (await _repository.IsUserOnline(uid))
                onlineUsers.Add(uid);
        }
        await Clients.Caller.SendAsync("OnlineUsers", onlineUsers);
    }

    // ── Voice channel presence ─────────────────────────────────────────────────

    /// <summary>
    /// Client calls this when joining a LiveKit voice room. clanId is null for DM voice
    /// rooms (voiceChannelId = "dm-{conversationId}"); presence is broadcast to
    /// "conversation_{conversationId}" instead of a clan group in that case.
    /// </summary>
    public async Task JoinVoiceChannel(string? clanId, string voiceChannelId, string userName)
    {
        var userId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(userId)) return;

        var group = VoiceBroadcastGroup(clanId, voiceChannelId);

        // Ensure the connection is in the broadcast group (may already be from SubscribeToClans/SubscribeToConversations)
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        await _repository.JoinVoiceChannel(Context.ConnectionId, userId, userName, clanId, voiceChannelId);

        _logger.LogInformation("User {UserId} joined voice channel {ChannelId} in clan {ClanId}", userId, voiceChannelId, clanId ?? "(dm)");

        await Clients.Group(group).SendAsync("UserJoinedVoice", new
        {
            clanId,
            voiceChannelId,
            userId,
            userName
        });
    }

    /// <summary>
    /// Client calls this when leaving a LiveKit voice room.
    /// </summary>
    public async Task LeaveVoiceChannel()
    {
        var info = await _repository.LeaveVoiceChannel(Context.ConnectionId);
        if (!info.HasValue) return;

        var (clanId, channelId, userId) = info.Value;

        _logger.LogInformation("User {UserId} left voice channel {ChannelId} in clan {ClanId}", userId, channelId, clanId ?? "(dm)");

        await Clients.Group(VoiceBroadcastGroup(clanId, channelId)).SendAsync("UserLeftVoice", new
        {
            clanId,
            voiceChannelId = channelId,
            userId
        });
    }

    /// <summary>
    /// Returns the current voice channel snapshot for a clan (e.g. on page refresh).
    /// </summary>
    public async Task GetVoiceChannelParticipants(string clanId)
    {
        var channels = await _repository.GetVoiceChannelParticipants(clanId);

        var participants = channels
            .SelectMany(kv => kv.Value.Select(u => new
            {
                voiceChannelId = kv.Key,
                userId = u.UserId,
                userName = u.UserName
            }))
            .ToList();

        await Clients.Caller.SendAsync("VoiceChannelParticipants", new
        {
            clanId,
            participants
        });
    }
}

