namespace PresenceService.Interfaces;

public interface IIdentityAuthorizationClient
{
    Task<IReadOnlySet<string>> GetFriendIdsAsync(string userId, CancellationToken cancellationToken);
}
