using System.Security.Claims;
using System.Text.Encodings.Web;
using IdentityService.Handlers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace IdentityServiceTests.UnitTests.Handlers;

public class GatewayAuthenticationHandlerTests
{
    [Fact]
    public async Task AuthenticateAsync_AddsGlobalRoleClaim()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "admin-1";
        context.Request.Headers["X-Global-Role"] = "SUPER_ADMIN";
        var handler = CreateHandler();
        await handler.InitializeAsync(
            new AuthenticationScheme(
                "GatewayAuth",
                null,
                typeof(GatewayAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(
            "admin-1",
            result.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(result.Principal.IsInRole("SUPER_ADMIN"));
    }

    [Fact]
    public async Task AuthenticateAsync_PreservesClanRoleAlongsideGlobalRole()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "admin-1";
        context.Request.Headers["X-Global-Role"] = "SUPER_ADMIN";
        context.Request.Headers["X-Clan-Role"] = "OWNER";
        var handler = CreateHandler();
        await handler.InitializeAsync(
            new AuthenticationScheme(
                "GatewayAuth",
                null,
                typeof(GatewayAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Principal.IsInRole("SUPER_ADMIN"));
        Assert.True(result.Principal.IsInRole("OWNER"));
    }

    private static GatewayAuthenticationHandler CreateHandler()
    {
        var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options
            .Setup(monitor => monitor.Get(It.IsAny<string>()))
            .Returns(new AuthenticationSchemeOptions());

        return new GatewayAuthenticationHandler(
            options.Object,
            LoggerFactory.Create(_ => { }),
            UrlEncoder.Default);
    }
}
