using IdentityService.Extensions;
using IdentityService.Messaging;
using IdentityService.Middlewares;
using Microsoft.AspNetCore.Builder;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Service configurations
builder.Services.AddApiConfiguration(); //  This already includes Swagger

// Using build service provider cause an warning so i use a logger 
// factory to create a logger instance and pass it to the database configuration method
using var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
});
var logger = loggerFactory.CreateLogger<Program>();

builder.Services.AddDatabaseConfiguration(builder.Configuration, logger);
builder.Services.AddIdentityConfiguration();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddTokenVersionRedis(builder.Configuration);



// RabbitMQ
builder.Services.AddRabbitMQProducer(builder.Configuration);

var app = builder.Build();

// Middleware configuration
app.UseMiddleware<PerformanceMiddleWare>();

// Pipeline configuration
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Identity Service API V1");
        c.RoutePrefix = string.Empty; // ✅ This should work now
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Database migration
await app.ApplyMigrationsAsync();
app.Run();

// Make Program accessible for integration tests
public partial class Program { }
