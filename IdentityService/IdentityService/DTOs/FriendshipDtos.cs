using System;
using System.ComponentModel.DataAnnotations;

namespace IdentityService.DTOs;

public class FriendRequestCreateDto
{
    [Required]
    public string AddresseeId { get; set; }
}

public class FriendshipReadDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; }
    public string UserName { get; set; }
    public string AvatarUrl { get; set; }
    public string Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
}
