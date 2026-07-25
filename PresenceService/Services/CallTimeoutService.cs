using MassTransit;
using Microsoft.AspNetCore.SignalR;
using PresenceService.Hubs;
using PresenceService.Interfaces;
using Shared.Contracts;

namespace PresenceService.Services;

public class CallTimeoutService(
    ICallRepository calls,
    IHubContext<PresenceHub> hubContext,
    IBus bus,
    ILogger<CallTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var call in calls.TimeoutExpired(DateTime.UtcNow.AddSeconds(-30)))
            {
                await CallEventSender.SendToBothAsync(
                    hubContext, call, "CallTimedOut", cancellationToken: stoppingToken);
                try
                {
                    await bus.Publish(new NotificationRequestedMessage
                    {
                        EventId = call.CallId,
                        UserId = call.CalleeUserId,
                        Type = NotificationType.MissedCall,
                        Title = "Missed voice call",
                        Body = "You missed a voice call.",
                        ActorUserId = call.CallerUserId,
                        TargetId = call.ConversationId,
                        CreatedAt = DateTime.UtcNow,
                    }, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Could not publish missed-call notification for {CallId}", call.CallId);
                }
            }
        }
    }
}
