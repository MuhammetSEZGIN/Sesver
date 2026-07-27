using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IdentityService.Models;

public enum FriendshipStatus
{
    Pending,
    Accepted,
    Blocked,
}

public class Friendship
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string RequesterId { get; set; }

    [Required]
    public string AddresseeId { get; set; }

    [Required]
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RespondedAt { get; set; }

    [ForeignKey(nameof(RequesterId))]
    public virtual ApplicationUser Requester { get; set; }

    [ForeignKey(nameof(AddresseeId))]
    public virtual ApplicationUser Addressee { get; set; }
}
