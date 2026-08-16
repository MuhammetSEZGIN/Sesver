using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Models;

public class ApplicationUser : IdentityUser
{
    public string AvatarUrl { get; set; }

    [MaxLength(200)]
    public string Bio { get; set; }

    [MaxLength(2048)]
    public string ProfileBackgroundUrl { get; set; }

    public int TokenVersion { get; set; }

    public virtual ICollection<UserRefreshToken> RefreshTokens { get; set; }
}
