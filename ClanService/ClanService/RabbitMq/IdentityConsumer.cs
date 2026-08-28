using System;
using MassTransit;
using ClanService.Interfaces;
using Shared.Contracts;
namespace ClanService.RabbitMq;

public class IdentityConsumer : IConsumer<UserUpdatedMessage>, IConsumer<UserDeletedMessage>
{
    ILogger<IdentityConsumer> _logger;
    IRabbitMqService   _rabbitMqService;

    public IdentityConsumer(ILogger<IdentityConsumer> logger, IRabbitMqService rabbitMqService)
    {
        _logger = logger;
        _rabbitMqService = rabbitMqService;
    }
    public async Task Consume(ConsumeContext<UserUpdatedMessage> context)
    {
        _logger.LogInformation("Received UserUpdatedMessage for user {UserName} with avatar {AvatarUrl}.",
            context.Message.userName, context.Message.AvatarUrl);
        await CreateIdentityAsync(context.Message);
    }

    public async Task Consume(ConsumeContext<UserDeletedMessage> context)
    {
        _logger.LogInformation(
            "Received UserDeletedMessage for user {UserId}.",
            context.Message.UserId);
        await _rabbitMqService.ConsumeUserDeleted(context.Message);
    }

    public async Task CreateIdentityAsync(UserUpdatedMessage message)
    {
        await _rabbitMqService.ConsumeUserInformation(message);       
    }
}
