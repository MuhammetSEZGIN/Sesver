using VersionControlService.Models;
using VersionControlService.Services;

namespace VersionControlService.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app, AdminAuthorizationOptions authOptions)
    {
        // Servis dogrudan disariya acik degil; kimlik ve global rol ApiGateway
        // tarafindan JWT'den cozulup X-* header'lari ile aktarilir. Gateway gelen
        // istekteki bu header'lari once temizledigi icin spoof edilemezler.
        var admin = app.MapGroup("/admin");

        admin.MapGet("/me", (HttpContext http) =>
        {
            var actor = ResolveActor(http, authOptions);
            return actor == null
                ? Forbidden()
                : Results.Ok(new AdminIdentityDto
                {
                    UserId = actor.UserId,
                    UserName = actor.UserName,
                    Role = actor.Role
                });
        });

        admin.MapGet("/releases", async (
            HttpContext http,
            ReleaseAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            if (ResolveActor(http, authOptions) == null)
            {
                return Forbidden();
            }

            var releases = await adminService.GetAllAsync(cancellationToken);
            return Results.Ok(releases);
        });

        admin.MapGet("/releases/targets", (HttpContext http, ReleaseAdminService adminService) =>
        {
            return ResolveActor(http, authOptions) == null
                ? Forbidden()
                : Results.Ok(adminService.GetTargets());
        });

        admin.MapPost("/releases", async (
            HttpContext http,
            SaveReleaseRequest request,
            ReleaseAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            var actor = ResolveActor(http, authOptions);
            if (actor == null)
            {
                return Forbidden();
            }

            var validationError = adminService.Validate(request);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var saved = await adminService.SaveAsync(request, actor.UserName, cancellationToken);
            return Results.Ok(saved);
        });

        admin.MapPut("/releases/{id:guid}", async (
            HttpContext http,
            Guid id,
            SaveReleaseRequest request,
            ReleaseAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            var actor = ResolveActor(http, authOptions);
            if (actor == null)
            {
                return Forbidden();
            }

            var existing = await adminService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
            {
                return Results.NotFound(new ErrorResponse("Surum bulunamadi"));
            }

            // Versiyon numarasi kaydin kimligi gibi kullaniliyor (unique index);
            // degistirmek yeni kayit yaratacagi icin engellenir.
            if (!string.Equals(existing.Version, request.Version.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse(
                    "Surum numarasi degistirilemez. Yeni bir surum olusturun."));
            }

            var validationError = adminService.Validate(request);
            if (validationError != null)
            {
                return Results.BadRequest(new ErrorResponse(validationError));
            }

            var saved = await adminService.SaveAsync(request, actor.UserName, cancellationToken);
            return Results.Ok(saved);
        });

        admin.MapPost("/releases/{id:guid}/publish", async (
            HttpContext http,
            Guid id,
            ReleaseAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            var actor = ResolveActor(http, authOptions);
            if (actor == null)
            {
                return Forbidden();
            }

            var updated = await adminService.SetLatestAsync(id, actor.UserName, cancellationToken);
            return updated
                ? Results.Ok(await adminService.GetByIdAsync(id, cancellationToken))
                : Results.NotFound(new ErrorResponse("Surum bulunamadi"));
        });

        admin.MapDelete("/releases/{id:guid}", async (
            HttpContext http,
            Guid id,
            ReleaseAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            var actor = ResolveActor(http, authOptions);
            if (actor == null)
            {
                return Forbidden();
            }

            var deleted = await adminService.DeleteAsync(id, actor.UserName, cancellationToken);
            return deleted
                ? Results.NoContent()
                : Results.NotFound(new ErrorResponse("Surum bulunamadi"));
        });

        return app;
    }

    private static AdminActor? ResolveActor(HttpContext http, AdminAuthorizationOptions authOptions)
    {
        var userId = http.Request.Headers["X-User-Id"].FirstOrDefault();
        var role = http.Request.Headers["X-Global-Role"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        if (!authOptions.AllowedRoles.Contains(role))
        {
            return null;
        }

        var userName = http.Request.Headers["X-User-Name"].FirstOrDefault();
        return new AdminActor(userId, string.IsNullOrWhiteSpace(userName) ? userId : userName, role);
    }

    private static IResult Forbidden() =>
        Results.Json(new ErrorResponse("Bu islem icin yetkiniz yok"), statusCode: StatusCodes.Status403Forbidden);

    private sealed record AdminActor(string UserId, string UserName, string Role);
}
