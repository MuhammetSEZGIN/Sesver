using Shared.Contracts;
using IdentityService.Interfaces;
using MassTransit;

namespace IdentityService.Messaging;

public class IdentityProducer :IIdentityProducer
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<IdentityProducer> _logger;

    public IdentityProducer(IPublishEndpoint publishEndpoint, ILogger<IdentityProducer> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishUserUpdatedMessageAsync(
        string userName,
        string avatarUrl,
        string userId
    )
    {
        var message = new UserUpdatedMessage
        {
            userId = userId,
            userName = userName,
            AvatarUrl = avatarUrl,
        };

        _logger.LogInformation(
            "Publishing UserUpdatedMessage for user: {userName}, avatarUrl: {avatarUrl}",
            userName,
            avatarUrl
        );
        await _publishEndpoint.Publish(message);
    }

    public Task PublishNotificationAsync(NotificationRequestedMessage message) =>
        _publishEndpoint.Publish(message);

    public Task PublishFriendshipRelationshipChangedAsync(FriendshipRelationshipChangedMessage message) =>
        _publishEndpoint.Publish(message);
}
