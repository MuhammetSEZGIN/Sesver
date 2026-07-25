using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Models;

public class ApplicationUser : IdentityUser
{
    public string AvatarUrl { get; set; }

    [MaxLength(190)]
    public string Bio { get; set; }
    public virtual ICollection<UserRefreshToken> RefreshTokens { get; set; }
}
