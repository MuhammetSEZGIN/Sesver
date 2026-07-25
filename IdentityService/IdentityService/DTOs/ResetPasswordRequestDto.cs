using System.ComponentModel.DataAnnotations;
using IdentityService.Attributes;

namespace IdentityService.DTOs;

public class ResetPasswordRequestDto
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string Email { get; set; }

    [Required(ErrorMessage = "Reset token is required.")]
    public string Token { get; set; }

    [Required(ErrorMessage = "New password is required.")]
    [PasswordValidation]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; }

    [Required(ErrorMessage = "Password confirmation is required.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    public string NewPasswordConfirmation { get; set; }
}
