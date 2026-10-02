namespace CRM.Data.Extensions;

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseCrmSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                headers["X-Permitted-Cross-Domain-Policies"] = "none";

                if (!headers.ContainsKey("Content-Security-Policy"))
                {
                    headers["Content-Security-Policy"] =
                        "default-src 'self'; " +
                        "base-uri 'self'; " +
                        "object-src 'none'; " +
                        "frame-ancestors 'none'; " +
                        "form-action 'self'; " +
                        "img-src 'self' data: blob: https:; " +
                        "font-src 'self' data: https:; " +
                        "style-src 'self' 'unsafe-inline'; " +
                        "script-src 'self' 'unsafe-inline' https://unpkg.com; " +
                        "connect-src 'self' ws: wss: https://graph.facebook.com https://graph.instagram.com;";
                }

                return Task.CompletedTask;
            });

            await next();
        });
    }
}
