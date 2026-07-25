using System.Text;
using IdentityService.Handlers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;

// JWT'nin gercek dogrulamasi ApiGateway'de yapiliyor; alt servisler (ClanService,
// MessageService ve artik IdentityService de) gateway'in enjekte ettigi X-User-Id /
// X-Clan-Role header'larina guveniyor (GatewayAuthenticationHandler, default scheme).
// JwtBearer semasi kayitli kaliyor ama sadece ileride servisin token'i dogrudan
// (gateway'siz) dogrulamasi gerekirse kullanilmak uzere - su an hicbir [Authorize]
// bunu varsayilan olarak kullanmiyor.
namespace IdentityService.Extensions
{
    public static class AuthenticationExtensions
    {
        public static IServiceCollection AddJwtAuthentication(
            this IServiceCollection services,
            IConfiguration configuration
        )
        {
            services
                .AddAuthentication("GatewayAuth")
                .AddScheme<AuthenticationSchemeOptions, GatewayAuthenticationHandler>(
                    "GatewayAuth",
                    null
                )
                .AddJwtBearer(
                    "JwtBearer",
                    jwtBearerOptions =>
                    {
                        jwtBearerOptions.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(
                                Encoding.UTF8.GetBytes(configuration["JWT:Key"]!)
                            ),
                            ValidateIssuer = true,
                            ValidIssuer = configuration["JWT:Issuer"],
                            ValidateAudience = true,
                            ValidAudience = configuration["JWT:Audience"],
                            ValidateLifetime = true,
                            ClockSkew = TimeSpan.FromMinutes(5),
                        };
                    }
                );

            return services;
        }
    }
}
