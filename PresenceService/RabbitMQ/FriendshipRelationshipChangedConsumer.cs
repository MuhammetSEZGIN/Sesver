using MassTransit;
using Microsoft.AspNetCore.SignalR;
using PresenceService.Hubs;
using PresenceService.Interfaces;
using PresenceService.Services;
using Shared.Contracts;

namespace PresenceService.RabbitMQ;

public class FriendshipRelationshipChangedConsumer(
    IPresenceRepository presence,
    ICallRepository calls,
    IHubContext<PresenceHub> hubContext) : IConsumer<FriendshipRelationshipChangedMessage>
{
    public async Task Consume(ConsumeContext<FriendshipRelationshipChangedMessage> context)
    {
        var message = context.Message;
        if (message.Status == FriendshipRelationshipStatus.Accepted ||
            string.IsNullOrWhiteSpace(message.UserAId) || string.IsNullOrWhiteSpace(message.UserBId))
            return;

        await RemoveWatchAsync(message.UserAId, message.UserBId);
        await RemoveWatchAsync(message.UserBId, message.UserAId);

        var call = calls.TerminateRelationship(message.UserAId, message.UserBId);
        if (call != null)
            await CallEventSender.SendToBothAsync(
                hubContext, call, "CallEnded", "friendship-changed", context.CancellationToken);
    }

    private async Task RemoveWatchAsync(string watcherUserId, string watchedUserId)
    {
        foreach (var connectionId in await presence.GetUserConnections(watcherUserId))
        {
            var watched = await presence.GetConnectionWatchedUsers(connectionId);
            if (!watched.Contains(watchedUserId, StringComparer.Ordinal)) continue;
            await hubContext.Groups.RemoveFromGroupAsync(connectionId, $"user_{watchedUserId}");
            await presence.RemoveWatchedUser(connectionId, watchedUserId);
        }
    }
}
