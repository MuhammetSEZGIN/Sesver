using System.Net.Http.Json;
using PresenceService.Interfaces;

namespace PresenceService.Services;

public class IdentityAuthorizationClient(HttpClient httpClient) : IIdentityAuthorizationClient
{
    public async Task<IReadOnlySet<string>> GetFriendIdsAsync(
        string userId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Friendship/friend-ids");
        request.Headers.Add("X-User-Id", userId);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var ids = await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken) ?? [];
        return ids.ToHashSet(StringComparer.Ordinal);
    }
}
