using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Data;
using NotificationService.DTOs;
using NotificationService.Hubs;
using NotificationService.Interfaces;
using NotificationService.Models;
using Shared.Contracts;

namespace NotificationService.Services;

public class NotificationManager(
    NotificationDbContext dbContext,
    IHubContext<NotificationHub> hubContext,
    ILogger<NotificationManager> logger) : INotificationService
{
    public async Task<PagedNotificationDto> GetAsync(
        string userId, bool unreadOnly, int page, int limit, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit <= 0 ? 20 : limit, 1, 100);
        var query = dbContext.Notifications.AsNoTracking().Where(x => x.UserId == userId);
        if (unreadOnly) query = query.Where(x => !x.IsRead);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * limit).Take(limit)
            .Select(x => ToDto(x)).ToListAsync(cancellationToken);
        return new PagedNotificationDto(items, page, limit, total);
    }

    public Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken) =>
        dbContext.Notifications.CountAsync(x => x.UserId == userId && !x.IsRead, cancellationToken);

    public async Task<bool> MarkReadAsync(string userId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Notifications.FirstOrDefaultAsync(
            x => x.Id == id && x.UserId == userId, cancellationToken);
        if (entity == null) return false;

        if (!entity.IsRead)
        {
            entity.IsRead = true;
            entity.ReadAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await PushUnreadCountAsync(userId, cancellationToken);
        }
        return true;
    }

    public async Task MarkAllReadAsync(string userId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await dbContext.Notifications.Where(x => x.UserId == userId && !x.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsRead, true)
                .SetProperty(x => x.ReadAt, now), cancellationToken);
        await hubContext.Clients.User(userId).SendAsync("UnreadCountChanged", 0, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string userId, Guid id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Notifications.FirstOrDefaultAsync(
            x => x.Id == id && x.UserId == userId, cancellationToken);
        if (entity == null) return false;
        var wasUnread = !entity.IsRead;
        dbContext.Notifications.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (wasUnread) await PushUnreadCountAsync(userId, cancellationToken);
        return true;
    }

    public async Task<bool> CreateAsync(NotificationRequestedMessage message, CancellationToken cancellationToken)
    {
        if (message.EventId == Guid.Empty || string.IsNullOrWhiteSpace(message.UserId))
        {
            logger.LogWarning("Ignoring invalid notification event {EventId}", message.EventId);
            return false;
        }

        if (await dbContext.Notifications.AnyAsync(x => x.Id == message.EventId, cancellationToken))
            return false;

        var entity = new NotificationEntity
        {
            Id = message.EventId,
            UserId = message.UserId,
            Type = message.Type,
            Title = Truncate(message.Title, 160),
            Body = Truncate(message.Body, 500),
            ActorUserId = TruncateNullable(message.ActorUserId, 450),
            TargetId = TruncateNullable(message.TargetId, 450),
            CreatedAt = message.CreatedAt == default ? DateTime.UtcNow : message.CreatedAt.ToUniversalTime(),
        };

        dbContext.Notifications.Add(entity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(entity).State = EntityState.Detached;
            if (await dbContext.Notifications.AsNoTracking()
                .AnyAsync(x => x.Id == message.EventId, cancellationToken))
            {
                return false;
            }
            throw;
        }

        var dto = ToDto(entity);
        try
        {
            await hubContext.Clients.User(message.UserId).SendAsync("ReceiveNotification", dto, cancellationToken);
            await PushUnreadCountAsync(message.UserId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notification {NotificationId} was saved but live push failed", entity.Id);
        }
        return true;
    }

    private async Task PushUnreadCountAsync(string userId, CancellationToken cancellationToken)
    {
        var count = await GetUnreadCountAsync(userId, cancellationToken);
        await hubContext.Clients.User(userId).SendAsync("UnreadCountChanged", count, cancellationToken);
    }

    private static NotificationDto ToDto(NotificationEntity entity) => new(
        entity.Id, entity.Type, entity.Title, entity.Body, entity.ActorUserId,
        entity.TargetId, entity.IsRead, entity.CreatedAt, entity.ReadAt);

    private static string Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value[..Math.Min(value.Length, maxLength)];

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value[..Math.Min(value.Length, maxLength)];
}
