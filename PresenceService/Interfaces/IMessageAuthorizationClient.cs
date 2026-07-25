using PresenceService.Models;

namespace PresenceService.Interfaces;

public interface IMessageAuthorizationClient
{
    Task<DmCallContext?> GetCallContextAsync(string conversationId, string accessToken, CancellationToken cancellationToken);
}
