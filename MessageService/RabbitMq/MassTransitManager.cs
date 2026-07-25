using System.Security.Authentication;
using MassTransit;
using Shared.Contracts;

namespace MessageService.RabbitMq;

public static class MassTransitManager
{
    public static IServiceCollection AddRabbitMQServices(this IServiceCollection services, IConfiguration configuration)
    {
        var rabbitMqOptions = new RabbitMqOptions();
        configuration.GetSection("RabbitMQ").Bind(rabbitMqOptions);
        services.AddMassTransit(x =>
         {
             x.AddConsumer<IdentityConsumer>();
             x.AddConsumer<ClanServiceConsumer>();

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

                // Retry mekanizması iki kuyruk için aynı olsun
                 cfg.UseMessageRetry(r =>
                {
                    r.Interval(3, TimeSpan.FromSeconds(10));
                });
                
                cfg.ReceiveEndpoint("Message-Service-UserUpdatedQueue", e =>
                {
                    e.ConfigureConsumer<IdentityConsumer>(context);
                });
                cfg.ReceiveEndpoint("Message-Service-ClanUpdatedQueue", e =>
                {
                    e.ConfigureConsumer<ClanServiceConsumer>(context);
                });
            
             });
         });

        return services;
    }
}
