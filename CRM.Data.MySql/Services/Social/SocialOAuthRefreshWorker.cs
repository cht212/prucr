namespace CRM.Data.Services;

public sealed class SocialOAuthRefreshWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SocialOAuthRefreshWorker> _logger;

    public SocialOAuthRefreshWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SocialOAuthRefreshWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await stoppingToken.WaitForDelayAsync(TimeSpan.FromSeconds(30)))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);

            if (!await stoppingToken.WaitForDelayAsync(TimeSpan.FromMinutes(30)))
            {
                break;
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var oauth = scope.ServiceProvider.GetRequiredService<SocialOAuthService>();
            if (await oauth.RefreshTikTokIfNeededAsync(cancellationToken))
                _logger.LogInformation("El token OAuth de TikTok se renovó correctamente.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cierre normal de la aplicación.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo renovar el token OAuth de TikTok.");
        }
    }
}
