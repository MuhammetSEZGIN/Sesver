using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApiGateway.Handlers;
using ApiGateway.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsEnvironment("Docker"))
{
    builder.Configuration.AddJsonFile("ocelot.docker.json", optional: false, reloadOnChange: true);
}
else
{
    builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
}

var corsConfigOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var corsDefaultOrigins = new[] { "http://localhost:5173", "tauri://localhost", "https://tauri.localhost","http://tauri.localhost" };
var corsAllowedOrigins = corsConfigOrigins.Union(corsDefaultOrigins).ToArray();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowTauri", policy =>
    {
        policy.WithOrigins(corsAllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // token ın güvenlir bir anahtarla imzalanıp imzalanmadığını kontrol eder
            ValidateIssuerSigningKey = true,
            // token imzansını doğrulamak için kullanılan anahtar
            // bu anahtar, JWT oluşturulurken kullanılan anahtarla aynı olmalıdır
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.ASCII.GetBytes(
                    builder.Configuration["JWT:Key"]!
                )
            ),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JWT:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JWT:Audience"],
            //Token'ın süresinin dolup dolmadığını kontrol eder.
            ValidateLifetime = true,
            // Token süresi kontrolünde sunucular arasındaki saat farklarına tolerans tanır
            // (burada sıfır olarak ayarlanmış, yani tolerans yok).
            ClockSkew = TimeSpan.Zero,
        };
    });
builder.Services.AddAuthorization();

// Ocelot, eşleşmeyen bir path için bile authentication middleware'ini
// route matching'den ÖNCE çalıştırıyor; bu yüzden var olmayan bir route'a
// yetkisiz istek atıldığında 404 yerine 401 dönüyor ve gerçek hata maskeleniyor.
// Çözüm: ocelot.json'daki UpstreamPathTemplate'lerden regex üretip, path hiçbiriyle
// eşleşmiyorsa auth'a hiç girmeden 404 dön.
var upstreamRoutePatterns = (builder.Configuration
    .GetSection("Routes")
    .Get<List<Dictionary<string, object>>>() ?? new List<Dictionary<string, object>>())
    .Select(route => route.TryGetValue("UpstreamPathTemplate", out var template) ? template?.ToString() : null)
    .Where(template => !string.IsNullOrEmpty(template))
    .Select(template => BuildUpstreamRegex(template!))
    .ToList();

static Regex BuildUpstreamRegex(string template)
{
    // {everything} -> .* (alt path segmentlerini de yutar), diğer {param}'lar -> [^/]+
    var pattern = Regex.Replace(template, @"\{everything\}", "__EVERYTHING__");
    pattern = Regex.Replace(pattern, @"\{[^/{}]+\}", "[^/]+");
    pattern = Regex.Escape(pattern).Replace("__EVERYTHING__", ".*");
    // Regex.Escape yukarıda [^/]+ içindeki karakterleri de escape eder, geri düzelt
    pattern = pattern.Replace(@"\[\^/\]\+", "[^/]+");
    return new Regex("^" + pattern + "/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

var authServiceBaseUrl = builder.Configuration["AuthService:BaseUrl"]
    ?? (builder.Environment.IsEnvironment("Docker")
        ? "http://authenticationservice:8081"
        : "http://localhost:8081");

builder.Services.AddHttpClient("AuthService", client =>
{
    client.BaseAddress = new Uri(authServiceBaseUrl);
});

var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? (builder.Environment.IsEnvironment("Docker")
        ? "session-redis:6379,abortConnect=false"
        : "localhost:6379,abortConnect=false");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisConnectionString)
);
builder.Services.AddSingleton<IGatewayTokenVersionStore, RedisGatewayTokenVersionStore>();

// 1. Önce yazdığımız Handler'ı sisteme (DI Container) tanıtıyoruz
//builder.Services.AddTransient<ApiGateway.Handlers.ClanRoleEnrichmentHandler>();

// 2. Sonra Ocelot'a bu Handler'ı kullanmasını söylüyoruz!
builder.Services
    .AddOcelot(builder.Configuration)
    .AddDelegatingHandler<RemoveDownstreamAuthorizationHandler>(true);
           
var app = builder.Build();

app.UseCors("AllowTauri");

// Gelen isteklerden sahte X-User-* header'larını temizle (header injection koruması)
app.Use(async (context, next) =>
{
    context.Request.Headers.Remove("X-User-Id");
    context.Request.Headers.Remove("X-User-Name");
    context.Request.Headers.Remove("X-User-Avatar");
    context.Request.Headers.Remove("X-Clan-Role");
    context.Request.Headers.Remove("X-Token-Version");
    await next();
});

// Route matching'i authentication'dan ÖNCE yap: path hiçbir Ocelot route'una
// eşleşmiyorsa 401 yerine 404 dön (bkz. yukarıdaki upstreamRoutePatterns yorumu).
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var matchesKnownRoute = upstreamRoutePatterns.Any(pattern => pattern.IsMatch(path));

    if (!matchesKnownRoute)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            title = "Route not found",
            status = 404,
            detail = $"No route matches path '{path}'."
        });
        return;
    }

    await next();
});

app.UseAuthentication();

// Parola değişince IdentityService Redis'teki sürümü artırır; Gateway eski
// sürümlü access token'ları ek bir servis/DB isteği yapmadan burada reddeder.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        await next();
        return;
    }

    var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.User.FindFirst("sub")?.Value;
    var tokenVersionValue = context.User.FindFirst("token_version")?.Value;

    if (
        string.IsNullOrWhiteSpace(userId)
        || !int.TryParse(tokenVersionValue, out var tokenVersion)
    )
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Session is no longer valid. Please sign in again."
        });
        return;
    }

    try
    {
        var tokenVersionStore = context.RequestServices
            .GetRequiredService<IGatewayTokenVersionStore>();
        var currentVersion = await tokenVersionStore.GetAsync(userId);

        if (!currentVersion.HasValue)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Session validation is temporarily unavailable."
            });
            return;
        }

        if (currentVersion.Value != tokenVersion)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Session is no longer valid. Please sign in again."
            });
            return;
        }
    }
    catch (RedisException)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            message = "Session validation is temporarily unavailable."
        });
        return;
    }

    await next();
});

app.UseAuthorization();

app.Use(
    async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            // 1. KULLANICI KİMLİĞİ KONTROLÜ
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                         ?? context.User.FindFirst("sub")?.Value 
                         ?? string.Empty;

            var userName = context.User.FindFirst(ClaimTypes.Name)?.Value 
                           ?? context.User.FindFirst("unique_name")?.Value 
                           ?? string.Empty;

            context.Request.Headers.Append("X-User-Id", userId);
            context.Request.Headers.Append("X-User-Name", userName);

            // ========================================================
            // 2. KÜRESEL CLAN ROLÜ KONTROLÜ (SENİN İSTEDİĞİN YER)
            // ========================================================
            var path = context.Request.Path.Value;
            var match = Regex.Match(path ?? "", @"/clanId/([a-fA-F0-9\-]{36})", RegexOptions.IgnoreCase);

            if (match.Success && !string.IsNullOrEmpty(userId))
            {
                var clanId = match.Groups[1].Value;
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                
                // Program.cs içinden HttpClient'ı çekiyoruz
                var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
                var authClient = httpClientFactory.CreateClient("AuthService");

                // Java'ya soruyoruz
                HttpResponseMessage response;
                try
                {
                    response = await authClient.GetAsync($"/roles?userId={userId}&clanId={clanId}");
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "AuthService rol sorgusu basarisiz. userId={UserId}, clanId={ClanId}", userId, clanId);
                    await next();
                    return;
                }

                logger.LogInformation(response.ToString());
                
               if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    
                    if (!string.IsNullOrWhiteSpace(jsonString))
                    {
                        try
                        {
                            // 1. Gelen metni JSON objesine çevir
                            using var jsonDoc = JsonDocument.Parse(jsonString);
                            
                            // 2. İçinde "roles" adında bir alan var mı diye bak
                            if (jsonDoc.RootElement.TryGetProperty("roles", out var roleElement))
                            {
                                var cleanRole = roleElement.GetString();
                                
                                if (!string.IsNullOrEmpty(cleanRole))
                                {
                                    // 3. Alt servislere sadece tertemiz "OWNER" veya "ADMIN" yazısını yolla!
                                    context.Request.Headers.Append("X-Clan-Role", cleanRole.ToUpper());
                                    logger.LogInformation("Zenginleştirilen Rol: {Role}", cleanRole);
                                }
                            }
                        }
                        catch (JsonException)
                        {
                            // Eğer Java'dan dönen şey geçerli bir JSON değilse (Düz metinse)
                            // Sistemin çökmesini engeller ve düz metin olarak eklemeyi dener
                            context.Request.Headers.Append("X-Clan-Role", jsonString.Trim().Trim('"').ToUpper());
                        }
                    }
                }
            }
        }
        await next();
    }
);

await app.UseOcelot();
app.Run();
