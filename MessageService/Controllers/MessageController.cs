using System.Security.Claims;
using MessageService.DTOs;
using MessageService.Interfaces;
using MessageService.Interfaces.Services;
using MessageService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Bson;

namespace MessageService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [EnableRateLimiting("fixed")]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    public class MessageController : ControllerBase
    {
        private readonly IMessageService _messageService;
        private readonly IDmConversationService _dmConversationService;

        public MessageController(
            IMessageService messageService,
            IDmConversationService dmConversationService
        )
        {
            _messageService = messageService;
            _dmConversationService = dmConversationService;
        }

        private async Task<bool> IsAuthorizedForDmAsync(string channelId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return await _dmConversationService.IsParticipantAsync(channelId, userId);
        }

        [HttpGet("channelId/{channelId}/clanId/{clanId}")]
        [Authorize(Roles = "OWNER,ADMIN,MEMBER")]
        public async Task<IActionResult> GetMessagesInChannelAsync(
            string channelId,
            string clanId,
            [FromQuery] int limit = 20,
            [FromQuery] int page = 1
        )
        {
            return await GetMessagesInternalAsync(channelId, clanId, limit, page);
        }

        [HttpGet]
        public async Task<IActionResult> GetMessagesByQueryAsync(
            [FromQuery] string channelId,
            [FromQuery] int limit = 20,
            [FromQuery] int page = 1
        )
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return BadRequest(new { message = "channelId is required" });
            }

            if (!_dmConversationService.IsDmConversationId(channelId))
            {
                return BadRequest(
                    new { message = "channelId must reference a DM conversation for this route" }
                );
            }

            if (!await IsAuthorizedForDmAsync(channelId))
            {
                return Forbid();
            }

            return await GetMessagesInternalAsync(channelId, null, limit, page);
        }

        private async Task<IActionResult> GetMessagesInternalAsync(
            string channelId,
            string clanId,
            int limit,
            int page
        )
        {
            var result = await _messageService.GetMessagesInChannelAsync(
                channelId,
                clanId,
                limit,
                page
            );

            if (!result.IsSuccess)
            {
                return StatusCode(result.StatusCode, new { message = result.Message });
            }

            var messageDtos = result
                .Data.Select(m => new MessageDto
                {
                    Id = m.Id,
                    ClanId = m.ClanId,
                    UserName = m.UserName ?? "Unknown",
                    ChannelId = m.ChannelId,
                    AvatarUrl = m.AvatarUrl ?? string.Empty,
                    SenderId = m.SenderId,
                    Text = m.Text,
                    CreatedAt = m.CreatedAt,
                })
                .ToList();

            return Ok(messageDtos);
        }

        [HttpDelete("{messageId}/clanId/{clanId}")]
        [Authorize(Roles = "OWNER,ADMIN,MEMBER")]
        public async Task<IActionResult> DeleteMessageAsync(ObjectId messageId, string clanId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await _messageService.DeleteMessageAsync(messageId, userId, clanId);

            if (!result.IsSuccess)
            {
                return StatusCode(result.StatusCode, new { message = result.Message });
            }

            return Ok(new { message = "Message deleted successfully" });
        }

        [HttpPut("{messageId}/clanId/{clanId}")]
        [Authorize(Roles = "OWNER,ADMIN,MEMBER")]
        public async Task<IActionResult> UpdateMessage(
            [FromBody] string message,
            ObjectId messageId,
            string clanId
        )
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var result = await _messageService.UpdateMessage(messageId, message, userId, clanId);
            if (!result.IsSuccess)
            {
                return StatusCode(result.StatusCode, new { message = result.Message });
            }
            return Ok(new { message = "Message updated successfully" });
        }
    }
}
