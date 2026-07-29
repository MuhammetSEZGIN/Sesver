using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ClanService.Handlers;

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

        var clanRole = Request.Headers["X-Clan-Role"].FirstOrDefault();
        var globalRole = Request.Headers["X-Global-Role"].FirstOrDefault();
        Logger.LogInformation(
            "[AUTH HANDLER] UserId: {UserId} | ClanRole: {ClanRole} | GlobalRole: {GlobalRole}",
            userId,
            clanRole ?? "NULL",
            globalRole ?? "NULL");

        var roles = new[] { clanRole, globalRole }
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
