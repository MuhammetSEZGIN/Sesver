using Shared.Contracts;

namespace IdentityService.Interfaces;

public interface IIdentityProducer
{
    Task PublishUserUpdatedMessageAsync(
            string userName,
            string avatarUrl,
            string userId
        );
    Task PublishNotificationAsync(NotificationRequestedMessage message);
    Task PublishFriendshipRelationshipChangedAsync(FriendshipRelationshipChangedMessage message);
}
