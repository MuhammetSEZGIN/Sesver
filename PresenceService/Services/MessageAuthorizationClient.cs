using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PresenceService.Interfaces;
using PresenceService.Models;

namespace PresenceService.Services;

public class MessageAuthorizationClient(HttpClient httpClient) : IMessageAuthorizationClient
{
    public async Task<DmCallContext?> GetCallContextAsync(
        string conversationId, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/Dm/conversations/{Uri.EscapeDataString(conversationId)}/call-context");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DmCallContext>(cancellationToken);
    }
}
