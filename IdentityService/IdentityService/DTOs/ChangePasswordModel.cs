using System.ComponentModel.DataAnnotations;
using IdentityService.Attributes;

namespace IdentityService.DTOs;

public class ChangePasswordModel
{
    [Required(ErrorMessage = "Current password is required.")]
    public string CurrentPassword { get; set; }

    [Required(ErrorMessage = "New password is required.")]
    [PasswordValidation]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; }
}
