using System.Security.Claims;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using IdentityService.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;

        public UserController(IUserService userService, IConfiguration configuration)
        {
            _userService = userService;
            _configuration = configuration;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var me = await _userService.GetMeAsync(userId);
            if (me == null)
            {
                return NotFound(new { Message = "User not found" });
            }
            return Ok(me);
        }

        /// <summary>
        /// Returns the public profile card of another user. Never exposes email,
        /// role claims or session data.
        /// </summary>
        [HttpGet("{userId}/profile")]
        public async Task<IActionResult> GetUserProfile(string userId)
        {
            var result = await _userService.GetUserProfileAsync(userId);
            return new ObjectResult(result) { StatusCode = result.StatusCode };
        }

        [HttpPut("update")]
        [ValidateModel]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await _userService.UpdateUserAsync(userId, model);
            if (result.Succeeded)
            {
                var profile = await _userService.GetMeAsync(userId);
                return Ok(new { Message = "User updated successfully", Data = profile });
            }
            return BadRequest(result.Errors);
        }

        [HttpPut("email")]
        [ValidateModel]
        public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequestDto model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var confirmationUrl = _configuration["ClientApp:EmailConfirmationUrl"];
            if (string.IsNullOrWhiteSpace(confirmationUrl))
            {
                confirmationUrl = $"{Request.Scheme}://{Request.Host}/api/Auth/confirm-email";
            }

            var result = await _userService.ChangeEmailAsync(userId, model, confirmationUrl);
            return new ObjectResult(result) { StatusCode = result.StatusCode };
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await _userService.ChangePasswordAsync(
                userId,
                model.CurrentPassword,
                model.NewPassword
            );
            return new ObjectResult(result) { StatusCode = result.StatusCode };
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchUsers(
            [FromQuery] string q,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20
        )
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var results = await _userService.SearchUsersAsync(userId, q, page, limit);
            return Ok(results);
        }

        [Authorize(Roles = "SUPER_ADMIN")]
        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteUser([FromHeader] string id)
        {
            var result = await _userService.DeleteUserAsync(id);
            if (result.Succeeded)
            {
                return Ok(new { Message = "User deleted successfully" });
            }
            return BadRequest(result.Errors);
        }
    }
}
