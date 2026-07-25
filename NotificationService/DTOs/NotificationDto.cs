using Shared.Contracts;

namespace NotificationService.DTOs;

public record NotificationDto(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    string? ActorUserId,
    string? TargetId,
    bool IsRead,
    DateTime CreatedAt,
    DateTime? ReadAt);

public record PagedNotificationDto(
    IReadOnlyList<NotificationDto> Items,
    int Page,
    int Limit,
    int Total);
