using System.Collections.Generic;
using System.Threading.Tasks;
using IdentityService.DTOs;

namespace IdentityService.Interfaces;

public interface IFriendshipService
{
    Task<List<FriendshipReadDto>> GetFriendsAsync(string userId);
    Task<List<FriendshipReadDto>> GetPendingRequestsAsync(string userId);
    Task<(bool Succeeded, string Message, FriendshipReadDto Data)> SendRequestAsync(
        string requesterId,
        string addresseeId
    );
    Task<(bool Succeeded, string Message)> AcceptRequestAsync(string userId, System.Guid requestId);
    Task<(bool Succeeded, string Message)> RejectRequestAsync(string userId, System.Guid requestId);
    Task<(bool Succeeded, string Message)> RemoveFriendAsync(string userId, string friendUserId);
}
