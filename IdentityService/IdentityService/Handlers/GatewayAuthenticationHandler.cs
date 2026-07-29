using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace IdentityService.Handlers;

public class GatewayAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public GatewayAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-User-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        };

        var globalRole = Request.Headers["X-Global-Role"].FirstOrDefault();
        if (!string.IsNullOrEmpty(globalRole))
        {
            claims.Add(new Claim(ClaimTypes.Role, globalRole));
        }

        var clanRole = Request.Headers["X-Clan-Role"].FirstOrDefault();
        if (!string.IsNullOrEmpty(clanRole))
        {
            claims.Add(new Claim(ClaimTypes.Role, clanRole));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
