using System.Security.Authentication;
using MassTransit;
using Shared.Contracts;

namespace NotificationService.RabbitMQ;

public static class MassTransitManager
{
    public static IServiceCollection AddNotificationRabbitMq(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("RabbitMQ").Get<RabbitMqOptions>() ?? new();
        services.AddMassTransit(x =>
        {
            x.AddConsumer<NotificationConsumer>();
            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(options.HostName, (ushort)options.Port, options.VirtualHost, host =>
                {
                    host.Username(options.UserName);
                    host.Password(options.Password);
                    if (options.Port == 5671)
                        host.UseSsl(ssl => ssl.Protocol = SslProtocols.Tls12);
                });
                cfg.UseMessageRetry(retry => retry.Interval(5, TimeSpan.FromSeconds(10)));
                cfg.ReceiveEndpoint("Notification-Service-RequestedQueue", endpoint =>
                {
                    endpoint.Durable = true;
                    endpoint.AutoDelete = false;
                    endpoint.ConfigureConsumer<NotificationConsumer>(context);
                });
            });
        });
        return services;
    }
}
