namespace IdentityService.DTOs;

public class UserMeDto
{
    public string Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public string Bio { get; set; }
    public string AvatarUrl { get; set; }
    public string ProfileBackgroundUrl { get; set; }
    public bool EmailConfirmed { get; set; }
}
