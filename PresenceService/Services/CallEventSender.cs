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
        CancellationToken cancellationToken = default)
    {
        var payload = Payload(call, reason);
        var caller = hubContext.Clients.Client(call.CallerConnectionId)
            .SendAsync(eventName, payload, cancellationToken);

        // Gelen arama henüz tek bir cihazda kabul edilmediyse tüm alıcı
        // oturumları çalar ve terminal olayı da hepsindeki zili kapatır.
        // Kabulden sonra ise medya yaşam döngüsü yalnızca aramayı başlatan ve
        // kabul eden SignalR bağlantılarına aittir.
        var callee = string.IsNullOrWhiteSpace(call.CalleeConnectionId)
            ? hubContext.Clients.User(call.CalleeUserId)
                .SendAsync(eventName, payload, cancellationToken)
            : hubContext.Clients.Client(call.CalleeConnectionId)
                .SendAsync(eventName, payload, cancellationToken);

        return Task.WhenAll(caller, callee);
    }
}
