using System.ComponentModel.DataAnnotations;

namespace IdentityService.DTOs
{
    public class UpdateUserModel
    {
        [Required]
        public string UserName { get; set; }

        [Required]
        public string Email { get; set; }

        public string AvatarUrl { get; set; }

        [MaxLength(190)]
        public string Bio { get; set; }
    }
}
