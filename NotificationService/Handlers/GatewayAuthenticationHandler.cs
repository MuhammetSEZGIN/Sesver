using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace NotificationService.Handlers;

/// <summary>
/// Authenticates REST requests from the API Gateway using headers that the
/// Gateway injects only after validating the caller's JWT.
/// </summary>
public sealed class GatewayAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public GatewayAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-User-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            Scheme.Name
        );
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            Scheme.Name
        );

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
