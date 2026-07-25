using System.Text;
using MessageService.Hubs;
using MessageService.RabbitMq;
using MessageService.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MessageService.Interfaces.Services;
using MessageService.Extensions;
using MessageService.Middlewares;
using Microsoft.AspNetCore.Authentication;
using MessageService.Handlers;

var builder = WebApplication.CreateBuilder(args);


// RabbitMQ 
builder.Services.AddRabbitMQServices(builder.Configuration);  
builder.Services.AddScoped<IRabbitMqService, RabbitMqService>();

builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

// MongoDB ve repository
builder.Services.AddMongoDbService(builder.Configuration);


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IMessageService, MessageService.Services.MessageService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDmConversationService, DmConversationService>();
builder.Services.AddSingleton<IMessageConnectionTracker, MessageConnectionTracker>();
builder.Services.AddSingleton<IBackgroundTaskQueue>(
    provider=> new BackgroundTaskQueue(capacity:100)
);
builder.Services.AddHostedService<QueuedHostedService>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

builder.Services.AddAuthentication("GatewayAuth")
    .AddScheme<AuthenticationSchemeOptions, GatewayAuthenticationHandler>("GatewayAuth", null)
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.ASCII.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/messagehub", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddSignalR();

// Rate Limiting
builder.Services.AddCustomRateLimiter();
// Cors
builder.Services.AddCustomCors(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors(); // Endpoint-level CORS only (e.g. RequireCors on SignalR hub)

// Normalize double slashes in request path before routing
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.Contains("//", StringComparison.Ordinal))
    {
        while (path.StartsWith("//", StringComparison.Ordinal))
        {
            path = path[1..];
        }
        context.Request.Path = new PathString(path);
    }
    await next();
});

app.MapHealthChecks("/health", new HealthCheckOptions()
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                exception = e.Value.Exception?.Message
            })
        };
        
        await context.Response.WriteAsJsonAsync(response);
    }
});
app.UseAuthentication();

app.UseRateLimiter();
app.UseAuthorization();
app.MapHub<MessageHub>("/messagehub").RequireCors("AllowTauri");
app.MapControllers();
app.Run();
