using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/realtime")]
[Authorize]
public sealed class RealtimeController : ControllerBase
{
    private readonly CrmRealtimeNotifier _notifier;

    public RealtimeController(CrmRealtimeNotifier notifier)
    {
        _notifier = notifier;
    }

    [HttpGet("events")]
    public async Task Events(CancellationToken cancellationToken)
    {
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var subscription = _notifier.Subscribe();
        try
        {
            await Response.WriteAsync("retry: 3000\nevent: connected\ndata: ready\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var available = subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
                var heartbeat = Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                var completed = await Task.WhenAny(available, heartbeat);

                if (completed == available && await available)
                {
                    long version = 0;
                    while (subscription.Reader.TryRead(out var current)) version = current;
                    await Response.WriteAsync($"event: changed\ndata: {version}\n\n", cancellationToken);
                }
                else
                {
                    await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                }

                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // El navegador cerró la conexión.
        }
        finally
        {
            _notifier.Unsubscribe(subscription.Id);
        }
    }
}
