using Microsoft.AspNetCore.SignalR;
using PresenceService.Hubs;
using PresenceService.Models;

namespace PresenceService.Services;

public static class CallEventSender
{
    public static object Payload(CallSession call, string? reason = null) => new
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

    public static Task SendToBothAsync(
        IHubContext<PresenceHub> hubContext,
        CallSession call,
        string eventName,
        string? reason = null,
        CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            hubContext.Clients.User(call.CallerUserId)
                .SendAsync(eventName, Payload(call, reason), cancellationToken),
            hubContext.Clients.User(call.CalleeUserId)
                .SendAsync(eventName, Payload(call, reason), cancellationToken));
}
