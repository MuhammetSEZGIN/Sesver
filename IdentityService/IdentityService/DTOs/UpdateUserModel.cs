using System.ComponentModel.DataAnnotations;
using IdentityService.Attributes;

namespace IdentityService.DTOs
{
    public class UpdateUserModel
    {
        [Required]
        public string UserName { get; set; }

        [MaxLength(2048)]
        public string AvatarUrl { get; set; }

        [MaxLength(200)]
        public string Bio { get; set; }

        [ImageUrl]
        public string ProfileBackgroundUrl { get; set; }
    }
}
