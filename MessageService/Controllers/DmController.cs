using System.Security.Claims;
using MessageService.DTOs;
using MessageService.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MessageService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [EnableRateLimiting("fixed")]
    public class DmController : ControllerBase
    {
        private readonly IDmConversationService _dmConversationService;

        public DmController(IDmConversationService dmConversationService)
        {
            _dmConversationService = dmConversationService;
        }

        [HttpPost("conversations")]
        public async Task<IActionResult> CreateOrGetConversation(
            [FromBody] DmConversationCreateDto model
        )
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(model.OtherUserId))
            {
                return BadRequest(new { message = "OtherUserId is required" });
            }
            if (model.OtherUserId == userId)
            {
                return BadRequest(new { message = "Cannot start a conversation with yourself" });
            }

            var conversation = await _dmConversationService.GetOrCreateConversationAsync(
                userId,
                model.OtherUserId
            );
            return Ok(conversation);
        }

        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var conversations = await _dmConversationService.GetConversationsAsync(userId);
            return Ok(conversations);
        }
    }
}
