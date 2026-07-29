using System.Security.Claims;
using IdentityService.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.Controllers;

[ApiController]
[Route("admin/users")]
[Authorize(Roles = "SUPER_ADMIN")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _adminUserService;

    public AdminUsersController(IAdminUserService adminUserService)
    {
        _adminUserService = adminUserService;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers(
        [FromQuery(Name = "q")] string query = null,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        var result = await _adminUserService.GetUsersAsync(query, page, limit);
        return Ok(result);
    }

    [HttpDelete("{userId}")]
    public async Task<IActionResult> DeleteUser(string userId)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await _adminUserService.DeleteUserAsync(actorUserId, userId);

        if (result.IsSuccessfull)
        {
            return NoContent();
        }

        return new ObjectResult(result) { StatusCode = result.StatusCode };
    }
}
