using System.ComponentModel.DataAnnotations;
using Shared.Contracts;

namespace NotificationService.Models;

public class NotificationEntity
{
    [Key]
    public Guid Id { get; set; }
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(500)]
    public string Body { get; set; } = string.Empty;
    [MaxLength(450)]
    public string? ActorUserId { get; set; }
    [MaxLength(450)]
    public string? TargetId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
