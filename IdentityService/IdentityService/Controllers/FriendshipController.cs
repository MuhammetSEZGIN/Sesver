using System;
using System.Security.Claims;
using IdentityService.DTOs;
using IdentityService.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class FriendshipController : ControllerBase
    {
        private readonly IFriendshipService _friendshipService;

        public FriendshipController(IFriendshipService friendshipService)
        {
            _friendshipService = friendshipService;
        }

        [HttpGet]
        public async Task<IActionResult> GetFriends()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var friends = await _friendshipService.GetFriendsAsync(userId);
            return Ok(friends);
        }

        [HttpGet("requests")]
        public async Task<IActionResult> GetPendingRequests()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var requests = await _friendshipService.GetPendingRequestsAsync(userId);
            return Ok(requests);
        }

        [HttpPost("requests")]
        public async Task<IActionResult> SendRequest([FromBody] FriendRequestCreateDto model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var (succeeded, message, data) = await _friendshipService.SendRequestAsync(
                userId,
                model.AddresseeId
            );
            if (!succeeded)
            {
                return BadRequest(new { Message = message });
            }
            return Ok(new { Message = message, Data = data });
        }

        [HttpPost("requests/{id}/accept")]
        public async Task<IActionResult> AcceptRequest(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var (succeeded, message) = await _friendshipService.AcceptRequestAsync(userId, id);
            if (!succeeded)
            {
                return NotFound(new { Message = message });
            }
            return Ok(new { Message = message });
        }

        [HttpPost("requests/{id}/reject")]
        public async Task<IActionResult> RejectRequest(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var (succeeded, message) = await _friendshipService.RejectRequestAsync(userId, id);
            if (!succeeded)
            {
                return NotFound(new { Message = message });
            }
            return Ok(new { Message = message });
        }

        [HttpDelete("{friendUserId}")]
        public async Task<IActionResult> RemoveFriend(string friendUserId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var (succeeded, message) = await _friendshipService.RemoveFriendAsync(
                userId,
                friendUserId
            );
            if (!succeeded)
            {
                return NotFound(new { Message = message });
            }
            return Ok(new { Message = message });
        }
    }
}
