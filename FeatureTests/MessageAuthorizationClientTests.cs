using System.Net;
using System.Text;
using PresenceService.Services;

namespace FeatureTests;

public class MessageAuthorizationClientTests
{
    [Fact]
    public async Task GetCallContextForwardsTrustedUserHeader()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://message-service")
        };
        var client = new MessageAuthorizationClient(httpClient);

        var result = await client.GetCallContextAsync("conversation-1", "user-1", CancellationToken.None);

        Assert.Equal("conversation-1", result?.ConversationId);
        Assert.Equal("user-2", result?.OtherUserId);
        Assert.Equal("user-1", handler.ForwardedUserId);
        Assert.Equal(
            "/api/Dm/conversations/conversation-1/call-context",
            handler.RequestPath);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? ForwardedUserId { get; private set; }
        public string? RequestPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ForwardedUserId = request.Headers.GetValues("X-User-Id").Single();
            RequestPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"conversationId\":\"conversation-1\",\"otherUserId\":\"user-2\"}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
