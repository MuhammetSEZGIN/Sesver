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

    public Task PublishUserDeletedMessageAsync(string userId)
    {
        _logger.LogInformation("Publishing UserDeletedMessage for user: {UserId}", userId);
        return _publishEndpoint.Publish(new UserDeletedMessage { UserId = userId });
    }

    public Task PublishNotificationAsync(NotificationRequestedMessage message) =>
        _publishEndpoint.Publish(message);

    public Task PublishFriendshipRelationshipChangedAsync(FriendshipRelationshipChangedMessage message) =>
        _publishEndpoint.Publish(message);
}
