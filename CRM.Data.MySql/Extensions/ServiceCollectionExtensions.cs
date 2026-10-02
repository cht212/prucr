using CRM.Data.Data;
using CRM.Data.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Threading.RateLimiting;

namespace CRM.Data.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCrmDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<CrmRealtimeNotifier>();
        services.AddSingleton<CrmRealtimeSaveChangesInterceptor>();

        services.AddDbContextPool<CrmDbContext>((serviceProvider, options) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Falta configurar ConnectionStrings:DefaultConnection para MySQL.");
            }

            options.UseMySQL(connectionString);
            options.AddInterceptors(serviceProvider.GetRequiredService<CrmRealtimeSaveChangesInterceptor>());
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        }, poolSize: 32);

        return services;
    }

    public static IServiceCollection AddCrmApplicationServices(
        this IServiceCollection services,
        IHostEnvironment environment)
    {
        var keyDirectory = new DirectoryInfo(Path.Combine(
            environment.ContentRootPath,
            "App_Data",
            "data-protection-keys"));
        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("CRM.Data")
            .PersistKeysToFileSystem(keyDirectory);
        if (OperatingSystem.IsWindows())
        {
            // SmarterASP puede no cargar el perfil del Application Pool en
            // todos los planes. El alcance de maquina evita depender de ese
            // perfil; App_Data debe conservar sus permisos privados de IIS.
            dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<WhatsAppService>();
        services.AddScoped<AuditoriaService>();
        services.AddScoped<CrmAccessService>();
        services.AddScoped<CrmPermissionService>();
        services.AddScoped<SocialInboundService>();
        services.AddScoped<MetaWebhookService>();
        services.AddHostedService<SocialInboxRecoveryWorker>();
        services.AddHttpClient<SocialPublicationsService>(client =>
            client.Timeout = TimeSpan.FromSeconds(15));
        services.AddSingleton<BotSettingsService>();
        services.AddSingleton<QuickReplyTemplatesService>();
        services.AddSingleton<SocialIntegrationService>();
        services.AddHttpClient<SocialOAuthService>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(CreateExternalHttpHandler);
        services.AddHostedService<SocialOAuthRefreshWorker>();
        services.AddSingleton<WhatsAppNumberRegistry>();
        services.AddScoped<IPasswordHasher<CRM.Data.Models.CrmUsuario>, PasswordHasher<CRM.Data.Models.CrmUsuario>>();

        services.AddScoped<R2StorageService>();

        services.AddHttpClient<WhatsAppCloudApiService>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(CreateExternalHttpHandler);

        services.AddHttpClient<MetaGraphApiService>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(CreateExternalHttpHandler);

        services.AddHttpClient<MetaMessagingService>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(CreateExternalHttpHandler);

        return services;
    }

    public static IServiceCollection AddCrmCookieAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        var useSecureCookies = !environment.IsDevelopment();

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "crm_hpd_session";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = useSecureCookies
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.LoginPath = "/login.html";
                options.AccessDeniedPath = "/login.html?error=acceso";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Cookie.MaxAge = options.ExpireTimeSpan;

                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddCrmRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy("api", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 240,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"success\":false,\"message\":\"Demasiadas solicitudes. Intenta nuevamente en unos segundos.\"}",
                    cancellationToken);
            };
        });

        return services;
    }

    public static IServiceCollection AddCrmForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto |
                ForwardedHeaders.XForwardedHost;

            // ASP.NET Core trusts loopback proxies by default. Add only the
            // addresses of the ingress used by the deployment.
            foreach (var address in configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
            {
                if (System.Net.IPAddress.TryParse(address, out var proxy))
                    options.KnownProxies.Add(proxy);
            }
        });

        return services;
    }

    public static IServiceCollection AddCrmCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("CRM", policy =>
            {
                var allowedOrigins =
                    configuration
                        .GetSection("Cors:AllowedOrigins")
                        .Get<string[]>() ?? [];

                if (allowedOrigins.Length > 0)
                {
                    policy
                        .WithOrigins(allowedOrigins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                    return;
                }

                policy.SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrWhiteSpace(origin))
                    {
                        return false;
                    }

                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                    {
                        return false;
                    }

                    var host = uri.Host.Trim();
                    return host is "localhost" or "127.0.0.1" or "[::1]";
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
            });
        });

        return services;
    }

    // HttpClientHandler evita que WinHTTP solicite un certificado de cliente
    // del almacén de Windows (error 12185) al conectar con las APIs de Meta.
    private static HttpMessageHandler CreateExternalHttpHandler() =>
        new HttpClientHandler();

    private static string GetClientPartition(HttpContext context)
    {
        var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            return $"user:{userId}";
        }

        var ip = context.Connection.RemoteIpAddress?.ToString();

        return $"ip:{ip ?? "unknown"}";
    }
}
