using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VersionControlService.Data;
using VersionControlService.Extensions;
using VersionControlService.Endpoints;
using VersionControlService.Repositories;
using VersionControlService.Serialization;
using VersionControlService.Services;

var builder = WebApplication.CreateSlimBuilder(args);

// Configure SQLite database — override with Database:Path env var in production
var dbPath = builder.Configuration["Database:Path"]
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Database", "version_control.db"));
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<VersionControlDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

var configuredTargets = builder.Configuration
    .GetSection("Release:Platforms")
    .GetChildren()
    .Select(section => section.Key)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

if (configuredTargets.Count == 0)
{
    configuredTargets = new[] { "windows-x86_64" }.ToHashSet(StringComparer.OrdinalIgnoreCase);
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// Register repository and service layer
builder.Services.AddScoped<IReleaseRepository, ReleaseRepository>();
builder.Services.AddScoped<ReleaseCatalogService>();

var adminAuthorizationOptions = new AdminAuthorizationOptions(
    builder.Configuration.GetSection("Admin:AllowedRoles").Get<string[]>());

builder.Services.AddScoped(serviceProvider => new ReleaseAdminService(
    serviceProvider.GetRequiredService<IReleaseRepository>(),
    configuredTargets,
    serviceProvider.GetRequiredService<ILogger<ReleaseAdminService>>()));

var app = builder.Build();

// Apply migrations and seed database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<VersionControlDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    var catalogService = scope.ServiceProvider.GetRequiredService<ReleaseCatalogService>();
    await catalogService.EnsureSeedAsync();
}
app.UseVersionControlResponseLogging();
app.UseStaticFiles();

app.MapVersionControlEndpoints(configuredTargets);
app.MapAdminEndpoints(adminAuthorizationOptions);

app.Run();
