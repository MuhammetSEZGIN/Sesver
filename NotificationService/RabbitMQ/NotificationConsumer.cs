using MassTransit;
using NotificationService.Interfaces;
using Shared.Contracts;

namespace NotificationService.RabbitMQ;

public class NotificationConsumer(INotificationService service) : IConsumer<NotificationRequestedMessage>
{
    public async Task Consume(ConsumeContext<NotificationRequestedMessage> context)
    {
        await service.CreateAsync(context.Message, context.CancellationToken);
    }
}
