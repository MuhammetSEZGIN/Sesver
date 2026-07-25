namespace Shared.Contracts;

public enum ChannelType
{
    VoiceChannel,
    TextChannel
}
public enum MessageType
{
    ClanDeleted,
    TextChannelDeleted,
    VoiceChannelDeleted,
}
public enum ClanRole
{
    MEMBER,
    ADMIN,
    OWNER
}
public enum ClanRoleEventType
{
    ASSIGN_ROLE,
    REMOVE_ROLE,
    REMOVE_ALL_ROLES
}

public enum NotificationType
{
    FriendRequestReceived,
    FriendRequestAccepted,
    DirectMessageReceived,
    ClanInvite,
    MissedCall
}

public enum FriendshipRelationshipStatus
{
    Accepted,
    Removed,
    Blocked
}

public record ClanRoleEventDto
{
    public string? UserId { get; init; }
    public string? ClanId { get; init; }
    public string? Role { get; init; }
    public string? EventType { get; init; }
}
public record class ChannelDeletedMessage
{
    public string? ChannelId { get; set; }
    public string? ClanId { get; set; }
    public ChannelType ChannelType { get; set; }
}

public record UserUpdatedMessage
{
    public string? userId { get; init; }
    public string? userName { get; init; }
    public string? AvatarUrl { get; init; }

}
public record ClanDeletedMessage
{
    public string? ClanId { get; init; }
}

public record NotificationRequestedMessage
{
    public Guid EventId { get; init; }
    public string? UserId { get; init; }
    public NotificationType Type { get; init; }
    public string? Title { get; init; }
    public string? Body { get; init; }
    public string? ActorUserId { get; init; }
    public string? TargetId { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record FriendshipRelationshipChangedMessage
{
    public Guid EventId { get; init; }
    public string? UserAId { get; init; }
    public string? UserBId { get; init; }
    public FriendshipRelationshipStatus Status { get; init; }
    public DateTime ChangedAt { get; init; }
}
