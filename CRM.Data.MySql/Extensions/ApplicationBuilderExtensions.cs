using Microsoft.AspNetCore.Diagnostics;

namespace CRM.Data.Extensions;

public static class ApplicationBuilderExtensions
{
    public static WebApplication UseCrmExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                if (exception != null)
                {
                    app.Logger.LogError(exception, "Error no controlado en el CRM.");
                }

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = app.Environment.IsDevelopment() && exception != null
                        ? exception.Message
                        : "Error interno del servidor.",
                    traceId = context.TraceIdentifier
                });
            });
        });

        return app;
    }
}
