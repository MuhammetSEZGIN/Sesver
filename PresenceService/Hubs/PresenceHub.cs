using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PresenceService.Interfaces;
using PresenceService.Models;

namespace PresenceService.Hubs;

[Authorize(AuthenticationSchemes = "Bearer")]
public class PresenceHub : Hub
{
    private const string DmVoiceRoomPrefix = "dm-";

    private readonly IPresenceRepository _repository;
    private readonly ILogger<PresenceHub> _logger;
    private readonly ICallRepository _calls;
    private readonly IIdentityAuthorizationClient _identityClient;
    private readonly IMessageAuthorizationClient _messageClient;

    public PresenceHub(
        IPresenceRepository repository,
        ICallRepository calls,
        IIdentityAuthorizationClient identityClient,
        IMessageAuthorizationClient messageClient,
        ILogger<PresenceHub> logger)
    {
        _repository = repository;
        _calls = calls;
        _identityClient = identityClient;
        _messageClient = messageClient;
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
            var becameOnline = await _repository.AddUserConnection(userId, Context.ConnectionId);
            _logger.LogInformation("User {UserId} connected", userId);
            if (becameOnline)
                await Clients.Group($"user_{userId}").SendAsync("UserOnline", userId);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
        {
            var clanIds = await _repository.GetConnectionClans(Context.ConnectionId);
            var becameOffline = await _repository.RemoveUserConnection(userId, Context.ConnectionId);

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

                if (string.IsNullOrEmpty(clanId) && channelId.StartsWith(DmVoiceRoomPrefix, StringComparison.Ordinal))
                {
                    var ended = _calls.EndAcceptedForUser(uid, channelId[DmVoiceRoomPrefix.Length..]);
                    if (ended.Succeeded)
                        await SendCallToBothAsync(ended.Call!, "CallEnded", "disconnected-from-voice");
                }
            }

            if (becameOffline)
            {
                await Clients.Group($"user_{userId}").SendAsync("UserOffline", userId);
                foreach (var clanId in clanIds)
                    await Clients.Group($"clan_{clanId}").SendAsync("UserOffline", userId);

                var disconnectedCall = _calls.HandleUserOffline(userId);
                if (disconnectedCall != null)
                {
                    var eventName = disconnectedCall.Status == CallStatus.Cancelled
                        ? "CallCancelled"
                        : "CallEnded";
                    await SendCallToBothAsync(disconnectedCall, eventName, "disconnected");
                }
            }

            await _repository.RemoveConnectionClans(Context.ConnectionId);
            await _repository.RemoveConnectionConversations(Context.ConnectionId);
            await _repository.RemoveConnectionWatchedUsers(Context.ConnectionId);
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

    /// <summary>Replaces this connection's friend-presence subscriptions and returns a snapshot.</summary>
    public async Task SubscribeToUsers(List<string> userIds)
    {
        var currentUserId = Context.UserIdentifier;
        if (string.IsNullOrEmpty(currentUserId)) return;

        try
        {
            var friends = await _identityClient.GetFriendIdsAsync(currentUserId, Context.ConnectionAborted);
            var allowed = (userIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal).Where(friends.Contains).ToList();
            var previous = await _repository.GetConnectionWatchedUsers(Context.ConnectionId);

            foreach (var removed in previous.Except(allowed, StringComparer.Ordinal))
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{removed}");
            foreach (var added in allowed.Except(previous, StringComparer.Ordinal))
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{added}");

            await _repository.SetConnectionWatchedUsers(Context.ConnectionId, allowed);
            await SendOnlineSnapshotAsync(allowed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not authorize presence subscription for {UserId}", currentUserId);
            await Clients.Caller.SendAsync("SubscriptionFailed", "authorization-unavailable");
        }
    }

    /// <summary>
    /// Returns which of the given userIds are currently online.
    /// </summary>
    public async Task GetOnlineUsers(List<string> userIds)
    {
        try
        {
            var currentUserId = Context.UserIdentifier;
            if (string.IsNullOrWhiteSpace(currentUserId)) return;
            var friends = await _identityClient.GetFriendIdsAsync(currentUserId, Context.ConnectionAborted);
            var allowed = (userIds ?? []).Distinct(StringComparer.Ordinal).Where(friends.Contains).ToList();
            await SendOnlineSnapshotAsync(allowed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not authorize online-user query for {UserId}", Context.UserIdentifier);
            await Clients.Caller.SendAsync("SubscriptionFailed", "authorization-unavailable");
        }
    }

    // ── DM voice calls ─────────────────────────────────────────────────────────

    public async Task CallUser(string conversationId)
    {
        var callerUserId = Context.UserIdentifier;
        if (string.IsNullOrWhiteSpace(callerUserId) || string.IsNullOrWhiteSpace(conversationId)) return;

        try
        {
            var token = GetAccessToken();
            var callContext = await _messageClient.GetCallContextAsync(
                conversationId, token, Context.ConnectionAborted);
            if (callContext == null)
            {
                await SendCallFailedAsync(conversationId, "not-a-participant");
                return;
            }

            var friends = await _identityClient.GetFriendIdsAsync(callerUserId, Context.ConnectionAborted);
            if (!friends.Contains(callContext.OtherUserId))
            {
                await SendCallFailedAsync(conversationId, "not-friends");
                return;
            }

            var result = _calls.TryCreate(conversationId, callerUserId, callContext.OtherUserId);
            if (!result.Succeeded)
            {
                await Clients.User(callerUserId).SendAsync("CallBusy", new
                {
                    conversationId,
                    targetUserId = callContext.OtherUserId,
                });
                return;
            }

            var call = result.Call!;
            await Clients.User(call.CalleeUserId).SendAsync("IncomingCall", CallPayload(call));
            await Clients.User(call.CallerUserId).SendAsync("CallRinging", CallPayload(call));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start call for conversation {ConversationId}", conversationId);
            await SendCallFailedAsync(conversationId, "authorization-unavailable");
        }
    }

    public Task AcceptCall(Guid callId) => ApplyCallActionAsync(
        _calls.Accept(callId, Context.UserIdentifier ?? string.Empty), "CallAccepted");

    public Task RejectCall(Guid callId) => ApplyCallActionAsync(
        _calls.Reject(callId, Context.UserIdentifier ?? string.Empty), "CallRejected");

    public Task CancelCall(Guid callId) => ApplyCallActionAsync(
        _calls.Cancel(callId, Context.UserIdentifier ?? string.Empty), "CallCancelled");

    public Task EndCall(Guid callId) => ApplyCallActionAsync(
        _calls.End(callId, Context.UserIdentifier ?? string.Empty), "CallEnded");

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

        if (string.IsNullOrEmpty(clanId) && channelId.StartsWith(DmVoiceRoomPrefix, StringComparison.Ordinal))
        {
            var ended = _calls.EndAcceptedForUser(userId, channelId[DmVoiceRoomPrefix.Length..]);
            if (ended.Succeeded)
                await SendCallToBothAsync(ended.Call!, "CallEnded", "left-voice");
        }
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

    private string GetAccessToken()
    {
        var token = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Hub access token is unavailable.");
        return token;
    }

    private async Task SendOnlineSnapshotAsync(IEnumerable<string> userIds)
    {
        var online = new List<string>();
        foreach (var userId in userIds)
            if (await _repository.IsUserOnline(userId)) online.Add(userId);
        await Clients.Caller.SendAsync("OnlineUsers", online);
    }

    private async Task ApplyCallActionAsync(CallActionResult result, string eventName)
    {
        if (!result.Succeeded)
        {
            await Clients.Caller.SendAsync("CallFailed", new { callId = result.Call?.CallId, code = result.Code });
            return;
        }
        await SendCallToBothAsync(result.Call!, eventName);
    }

    private Task SendCallToBothAsync(CallSession call, string eventName, string? reason = null) =>
        Task.WhenAll(
            Clients.User(call.CallerUserId).SendAsync(eventName, CallPayload(call, reason)),
            Clients.User(call.CalleeUserId).SendAsync(eventName, CallPayload(call, reason)));

    private static object CallPayload(CallSession call, string? reason = null) => new
    {
        callId = call.CallId,
        conversationId = call.ConversationId,
        callerUserId = call.CallerUserId,
        calleeUserId = call.CalleeUserId,
        roomId = call.RoomId,
        status = call.Status.ToString(),
        createdAt = call.CreatedAt,
        expiresAt = call.CreatedAt.AddSeconds(30),
        reason,
    };

    private Task SendCallFailedAsync(string conversationId, string code) =>
        Clients.Caller.SendAsync("CallFailed", new { conversationId, code });
}
