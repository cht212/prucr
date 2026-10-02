namespace CRM.Data.Extensions;

public static class RequestSecurityExtensions
{
    private static readonly HashSet<string> UnsafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    public static IApplicationBuilder UseCrmRequestSecurity(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                    context.Response.Headers["Pragma"] = "no-cache";
                    return Task.CompletedTask;
                });
            }

            if (DebeBloquearPeticionCrossSite(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"success\":false,\"message\":\"Solicitud rechazada por politica de seguridad.\"}");
                return;
            }

            await next();
        });
    }

    private static bool DebeBloquearPeticionCrossSite(HttpContext context)
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api")) return false;
        if (!UnsafeMethods.Contains(request.Method)) return false;
        if (EsWebhookExterno(request.Path)) return false;
        // El login no depende de una sesión existente y puede servirse detrás de
        // un proxy cuyo host externo sea distinto al que recibe Kestrel. CORS y
        // la limitación de intentos siguen protegiendo este endpoint.
        if (request.Path.StartsWithSegments("/api/auth/login")) return false;

        var origin = request.Headers.Origin.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(origin) && !CoincideConHostActual(origin, request))
        {
            return true;
        }

        var referer = request.Headers.Referer.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(referer) && !CoincideConHostActual(referer, request))
        {
            return true;
        }

        var secFetchSite = request.Headers["Sec-Fetch-Site"].FirstOrDefault();
        return secFetchSite is "cross-site" or "none" &&
            string.IsNullOrWhiteSpace(origin) &&
            string.IsNullOrWhiteSpace(referer);
    }

    private static bool EsWebhookExterno(PathString path) =>
        path.StartsWithSegments("/api/whatsapp/webhook") ||
        path.StartsWithSegments("/api/integraciones/meta/webhook") ||
        path.StartsWithSegments("/api/integraciones/tiktok/webhook");

    private static bool CoincideConHostActual(string value, HttpRequest request)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Host.Equals(request.Host.Host, StringComparison.OrdinalIgnoreCase);
    }
}
