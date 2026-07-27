using System.ComponentModel.DataAnnotations;

namespace IdentityService.DTOs;

public class ForgotPasswordRequestDto
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string Email { get; set; }
}
