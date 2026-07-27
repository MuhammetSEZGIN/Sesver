using NotificationService.DTOs;
using Shared.Contracts;

namespace NotificationService.Interfaces;

public interface INotificationService
{
    Task<PagedNotificationDto> GetAsync(string userId, bool unreadOnly, int page, int limit, CancellationToken cancellationToken);
    Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken);
    Task<bool> MarkReadAsync(string userId, Guid id, CancellationToken cancellationToken);
    Task MarkAllReadAsync(string userId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string userId, Guid id, CancellationToken cancellationToken);
    Task<int> DeleteAllAsync(string userId, CancellationToken cancellationToken);
    Task<bool> CreateAsync(NotificationRequestedMessage message, CancellationToken cancellationToken);
}
