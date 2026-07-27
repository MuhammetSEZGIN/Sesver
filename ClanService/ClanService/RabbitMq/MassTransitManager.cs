using MassTransit;
using System.Security.Authentication;
using System.Text.Json;
using Shared.Contracts;

namespace ClanService.RabbitMq;

public static class MassTransitManager
{
    public static IServiceCollection AddRabbitMQServices(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbitMqOptions = new RabbitMqOptions();
        configuration.GetSection("RabbitMQ").Bind(rabbitMqOptions);
        services.AddMassTransit(x =>
                 {
                     x.AddConsumer<IdentityConsumer, SubmitIdentityConsumeDefinition>();

                     x.UsingRabbitMq((context, cfg) =>
                     {
                         cfg.Host(rabbitMqOptions.HostName, (ushort)rabbitMqOptions.Port, rabbitMqOptions.VirtualHost, h =>
                         {
                             h.Username(rabbitMqOptions.UserName);
                             h.Password(rabbitMqOptions.Password);
                             if (rabbitMqOptions.Port == 5671)
                             {
                                 h.UseSsl(s =>
                                 {
                                     s.Protocol = SslProtocols.Tls12;
                                 });
                             }
                         });
                         cfg.UseMessageRetry(r =>
                 {
                     r.Interval(5, TimeSpan.FromSeconds(10));
                 });
                         cfg.ConfigureEndpoints(context);
                     });
                 });
        return services;
    }
}
