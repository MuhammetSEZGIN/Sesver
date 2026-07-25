using System.Reflection;
using IdentityService.Data;
using IdentityService.Examples;
using IdentityService.Interfaces;
using IdentityService.Messaging;
using IdentityService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Filters;

namespace IdentityService.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApiConfiguration(this IServiceCollection services)
        {
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.SuppressModelStateInvalidFilter = true;
            });

            // this also will call AddApiExplorer
            services.AddControllers();
            services.AddEndpointsApiExplorer();

            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc(
                    "v1",
                    new OpenApiInfo
                    {
                        Title = "Identity Service API",
                        Version = "v1",
                        Description = "Identity Service for user authentication and management",
                    }
                );
                // using System.Reflection;
                var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
                options.ExampleFilters();
            });

            services.AddSwaggerExamplesFromAssemblies(Assembly.GetExecutingAssembly());
            services.AddHttpContextAccessor();
            return services;
        }

        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<IIdentityProducer, IdentityProducer>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IdentityDbContext>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddScoped<IRefreshTokenService, RefreshTokenService>();
            services.AddScoped<IIpAddressService, IpAddressService>();
            services.AddScoped<IRegisterService, RegisterService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IFriendshipService, FriendshipService>();

            return services;
        }
    }
}
