using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Interfaces;
using NotificationService.DTOs;

namespace NotificationService.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = "GatewayAuth")]
public class NotificationController(INotificationService service) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public Task<PagedNotificationDto> Get(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default) =>
        service.GetAsync(UserId, unreadOnly, page, limit, cancellationToken);

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken) =>
        Ok(new { count = await service.GetUnreadCountAsync(UserId, cancellationToken) });

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken) =>
        await service.MarkReadAsync(UserId, id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await service.MarkAllReadAsync(UserId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await service.DeleteAsync(UserId, id, cancellationToken) ? NoContent() : NotFound();

    [HttpDelete]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        await service.DeleteAllAsync(UserId, cancellationToken);
        return NoContent();
    }
}
