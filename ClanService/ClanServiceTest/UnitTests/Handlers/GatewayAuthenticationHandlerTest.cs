using System.Security.Claims;
using System.Text.Encodings.Web;
using ClanService.Handlers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ClanServiceTest.UnitTests.Handlers;

public class GatewayAuthenticationHandlerTest
{
    [Fact]
    public async Task AuthenticateAsync_Adds_Global_Role_Claim()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "admin-user";
        context.Request.Headers["X-Global-Role"] = "SUPER_ADMIN";
        var handler = await CreateHandlerAsync(context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(
            "admin-user",
            result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(result.Principal?.IsInRole("SUPER_ADMIN"));
    }

    [Fact]
    public async Task AuthenticateAsync_Preserves_Clan_And_Global_Roles()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "owner-admin-user";
        context.Request.Headers["X-Clan-Role"] = "OWNER";
        context.Request.Headers["X-Global-Role"] = "SUPER_ADMIN";
        var handler = await CreateHandlerAsync(context);

        var result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.Principal?.IsInRole("OWNER"));
        Assert.True(result.Principal?.IsInRole("SUPER_ADMIN"));
    }

    private static async Task<GatewayAuthenticationHandler> CreateHandlerAsync(
        HttpContext context)
    {
        var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options
            .Setup(monitor => monitor.Get(It.IsAny<string>()))
            .Returns(new AuthenticationSchemeOptions());

        var handler = new GatewayAuthenticationHandler(
            options.Object,
            NullLoggerFactory.Instance,
            UrlEncoder.Default);
        await handler.InitializeAsync(
            new AuthenticationScheme(
                "GatewayAuth",
                null,
                typeof(GatewayAuthenticationHandler)),
            context);

        return handler;
    }
}
