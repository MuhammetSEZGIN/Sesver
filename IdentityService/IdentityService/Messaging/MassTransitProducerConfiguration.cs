using System.Security.Authentication;
using MassTransit;
using Shared.Contracts;

namespace IdentityService.Messaging;

 public static class MassTransitProducerConfiguration
    {
        public static IServiceCollection AddRabbitMQProducer(this IServiceCollection services, IConfiguration configuration)
        {
            var rabbitMqOptions = new RabbitMqOptions();
            configuration.GetSection("RabbitMQ").Bind(rabbitMqOptions);
            
            services.AddMassTransit(x =>
            {
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
                });
            });
            
            return services;
        }
    }
