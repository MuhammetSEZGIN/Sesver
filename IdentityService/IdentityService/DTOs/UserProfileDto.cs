namespace IdentityService.DTOs;

/// <summary>
/// Public profile card of a user. Deliberately carries no email, role or session data:
/// it is readable by any authenticated user.
/// </summary>
public class UserProfileDto
{
    public string Id { get; set; }
    public string UserName { get; set; }
    public string AvatarUrl { get; set; }
    public string Bio { get; set; }
    public string ProfileBackgroundUrl { get; set; }
}
