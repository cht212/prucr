using CRM.Data.DTOs.Social;
using CRM.Data.Models;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/integraciones")]
public sealed class IntegracionesController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MarketingCacheLocks = new();

    private readonly IConfiguration _configuration;
    private readonly BotSettingsService _botSettings;
    private readonly SocialIntegrationService _socialIntegrations;
    private readonly SocialOAuthService _socialOAuth;
    private readonly SocialInboundService _inbound;
    private readonly MetaGraphApiService _metaGraph;
    private readonly MetaWebhookService _metaWebhook;
    private readonly AuditoriaService _auditoria;
    private readonly ILogger<IntegracionesController> _logger;
    private readonly SocialPublicationsService _publications;
    private readonly WhatsAppNumberRegistry _numbers;
    private readonly IMemoryCache _cache;

    public IntegracionesController(
        IConfiguration configuration,
        BotSettingsService botSettings,
        SocialIntegrationService socialIntegrations,
        SocialOAuthService socialOAuth,
        SocialInboundService inbound,
        MetaGraphApiService metaGraph,
        MetaWebhookService metaWebhook,
        AuditoriaService auditoria,
        SocialPublicationsService publications,
        WhatsAppNumberRegistry numbers,
        IMemoryCache cache,
        ILogger<IntegracionesController> logger)
    {
        _configuration = configuration;
        _botSettings = botSettings;
        _socialIntegrations = socialIntegrations;
        _socialOAuth = socialOAuth;
        _inbound = inbound;
        _metaGraph = metaGraph;
        _metaWebhook = metaWebhook;
        _auditoria = auditoria;
        _logger = logger;
        _publications = publications;
        _numbers = numbers;
        _cache = cache;
    }

    [HttpGet("estado")]
    [Authorize]
    public IActionResult Estado()
    {
        bool TieneValor(string key) =>
            !string.IsNullOrWhiteSpace(_socialIntegrations.GetConfiguredValue(key) ?? _configuration[key]);

        return Ok(new
        {
            api = "OK",
            r2 = new
            {
                accountId = TieneValor("R2:AccountId"),
                accessKeyId = TieneValor("R2:AccessKeyId"),
                secretAccessKey = TieneValor("R2:SecretAccessKey"),
                bucketName = TieneValor("R2:BucketName")
            },
            whatsapp = new
            {
                verifyToken = TieneValor("WhatsApp:WebhookVerifyToken"),
                accessToken = TieneValor("WhatsApp:AccessToken"),
                phoneNumberId = _numbers.GetNumbers().Count > 0,
                numbers = _numbers.GetNumbers(),
                businessAccountId = TieneValor("WhatsApp:BusinessAccountId"),
                apiVersion = _socialIntegrations.GetConfiguredValue("WhatsApp:ApiVersion") ??
                    _configuration["WhatsApp:ApiVersion"] ?? "v25.0",
                sendMessagesToMeta =
                    bool.TryParse(
                        _socialIntegrations.GetConfiguredValue("WhatsApp:SendMessagesToMeta") ??
                            _configuration["WhatsApp:SendMessagesToMeta"],
                        out var enabled) &&
                    enabled
            },
            bot = new
            {
                whatsappAutoReply = _botSettings.WhatsAppAutoReplyEnabled,
                whatsappMessage = _botSettings.WhatsAppAutoReplyMessage,
                maxAutoReplies = _botSettings.MaxAutoRepliesPerConversation
            },
            canales = _socialIntegrations.GetChannels(Request)
        });
    }

    [HttpGet("canales")]
    [Authorize]
    public IActionResult Canales()
    {
        return Ok(_socialIntegrations.GetChannels(Request));
    }

    [HttpGet("{canal}/oauth/start")]
    [Authorize]
    public IActionResult OAuthStart(string canal)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        try
        {
            return Ok(_socialOAuth.CreateStart(Request, canal, userId));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    [HttpGet("{canal}/configuracion")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleConnections)]
    public async Task<IActionResult> Configuracion(string canal, [FromQuery] bool revealSecrets = false)
    {
        if (revealSecrets && !User.IsInRole("Administrador"))
        {
            return Forbid();
        }

        if (revealSecrets)
        {
            int? usuarioId = int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var parsedUsuarioId)
                ? parsedUsuarioId
                : null;

            await _auditoria.RegistrarAsync(
                "Integracion",
                0,
                "LECTURA_SECRETOS",
                null,
                CanalSocial.Normalizar(canal),
                usuarioId);
        }

        return Ok(_socialIntegrations.GetConfiguration(canal, revealSecrets));
    }

    [HttpPut("{canal}/configuracion")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ManageIntegrations)]
    public IActionResult GuardarConfiguracion(string canal, SocialIntegrationSaveDto dto)
    {
        SocialChannelConfiguration updated;
        try
        {
            updated = _socialIntegrations.SaveConfiguration(
                canal,
                dto.Values ?? new Dictionary<string, string?>());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        return Ok(new
        {
            success = true,
            configuration = updated
        });
    }

    [HttpGet("{canal}/oauth/callback")]
    [Authorize]
    public async Task<IActionResult> OAuthCallback(
        string canal,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest(new
            {
                success = false,
                canal,
                error,
                detail = errorDescription
            });
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return BadRequest(new { success = false, message = "Faltan code o state en el retorno OAuth." });
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        try
        {
            var result = await _socialOAuth.ExchangeAsync(
                Request, canal, userId, code, state, cancellationToken);
            await _auditoria.RegistrarAsync(
                "Integracion", 0, "OAUTH_CONECTADO", null, result.Canal,
                int.TryParse(userId, out var parsedUserId) ? parsedUserId : null);
            return Content(BuildOAuthResultHtml(true, result.Message), "text/html; charset=utf-8");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "No se pudo completar OAuth para {Canal}.", canal);
            return Content(BuildOAuthResultHtml(false, ex.Message), "text/html; charset=utf-8");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "El proveedor OAuth no respondió para {Canal}.", canal);
            return Content(
                BuildOAuthResultHtml(false, "No se pudo contactar al proveedor. Inténtalo nuevamente."),
                "text/html; charset=utf-8");
        }
    }

    private static string BuildOAuthResultHtml(bool success, string message)
    {
        var title = success ? "Conexión completada" : "No se pudo conectar";
        var color = success ? "#16803d" : "#b42318";
        return $$"""
            <!doctype html><html lang="es"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{title}}</title></head>
            <body style="font-family:Segoe UI,sans-serif;background:#eef4fa;padding:32px;color:#24323b">
              <main style="max-width:560px;margin:10vh auto;background:white;padding:32px;border-radius:18px;box-shadow:0 20px 50px #1232">
                <h1 style="color:{{color}}">{{title}}</h1>
                <p>{{HtmlEncoder.Default.Encode(message)}}</p>
                <a href="/" style="color:#003da5;font-weight:700">Volver al CRM</a>
              </main>
            </body></html>
            """;
    }

    [HttpGet("meta/webhook")]
    [AllowAnonymous]
    public IActionResult VerificarMetaWebhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var expected = _socialIntegrations.GetConfiguredValue("Meta:WebhookVerifyToken") ??
            _configuration["Meta:WebhookVerifyToken"];

        if (mode == "subscribe" &&
            !string.IsNullOrWhiteSpace(expected) &&
            token == expected)
        {
            return Content(challenge ?? string.Empty);
        }

        return Unauthorized();
    }

    [HttpGet("meta/estadisticas")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> EstadisticasMeta(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var key = $"marketing:meta:{desde?.Date:yyyyMMdd}:{hasta?.Date:yyyyMMdd}";
        var (dashboard, cacheStatus) = await GetMarketingDataAsync(
            key,
            refresh,
            async () =>
            {
                var result = await _metaGraph.ObtenerDashboardAsync(desde, hasta, cancellationToken);
                var duration = result.Success ? TimeSpan.FromMinutes(30) : TimeSpan.FromSeconds(10);
                return (result, duration);
            },
            cancellationToken);
        Response.Headers["X-CRM-Cache"] = cacheStatus;
        return Ok(dashboard);
    }

    [HttpGet("publicaciones/estadisticas")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> EstadisticasPublicaciones(
        [FromQuery] DateOnly? desde,
        [FromQuery] DateOnly? hasta,
        [FromQuery] bool refresh,
        CancellationToken cancellationToken)
    {
        var end = hasta ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var start = desde ?? end.AddDays(-30);
        if (start > end || end.DayNumber - start.DayNumber > 365 || end > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            return BadRequest(new { message = "El rango debe ser valido y no superar 365 dias." });
        var key = $"marketing:publicaciones:{start:yyyyMMdd}:{end:yyyyMMdd}";
        var (report, cacheStatus) = await GetMarketingDataAsync(
            key,
            refresh,
            async () =>
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                budget.CancelAfter(TimeSpan.FromSeconds(12));
                var result = await _publications.GetReportAsync(start, end, budget.Token);
                var duration = result.Canales.Any(channel =>
                    !string.IsNullOrWhiteSpace(channel.Error) ||
                    !string.IsNullOrWhiteSpace(channel.CommentsError))
                    ? TimeSpan.FromSeconds(10)
                    : TimeSpan.FromMinutes(30);
                return (result, duration);
            },
            cancellationToken);
        Response.Headers["X-CRM-Cache"] = cacheStatus;
        return Ok(report);
    }

    [HttpPost("publicaciones/{canal}/{publicationId}/comentarios/sincronizar")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> SincronizarComentariosPublicacion(
        string canal,
        string publicationId,
        CancellationToken cancellationToken)
    {
        var result = await _publications.SyncPublicationCommentsAsync(
            canal,
            publicationId,
            cancellationToken);
        if (!result.Success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                success = false,
                message = result.Error ?? "Instagram no permitió consultar los comentarios."
            });
        }

        return Ok(new
        {
            success = true,
            count = result.Comments.Count
        });
    }

    private async Task<(T Value, string Status)> GetMarketingDataAsync<T>(
        string key,
        bool refresh,
        Func<Task<(T Value, TimeSpan Duration)>> factory,
        CancellationToken cancellationToken)
    {
        T? cached;
        if (refresh)
        {
            _cache.Remove(key);
        }
        else if (_cache.TryGetValue<T>(key, out cached) && cached is not null)
        {
            return (cached, "HIT");
        }

        var gate = MarketingCacheLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue<T>(key, out cached) && cached is not null)
            {
                return (cached, "COALESCED");
            }

            var (value, duration) = await factory();
            _cache.Set(key, value, duration);
            return (value, refresh ? "REFRESH" : "MISS");
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpGet("meta/facebook/feed")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> FeedFacebook(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] int limit = 10)
    {
        return Ok(await _metaGraph.ObtenerFacebookFeedAsync(desde, hasta, limit));
    }

    [HttpGet("meta/estadisticas/diagnostico")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> DiagnosticoEstadisticasMeta(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null)
    {
        var diagnostico = await _metaGraph.DiagnosticarInsightsAsync(desde, hasta);
        return Ok(diagnostico);
    }

    [HttpGet("meta/credenciales/diagnostico")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleConnections)]
    public async Task<IActionResult> DiagnosticoCredencialesMeta()
    {
        return Ok(await _metaGraph.DiagnosticarCredencialesAsync());
    }

    [HttpPost("instagram/sincronizar")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> SincronizarInstagramLogin()
    {
        var result = await _metaGraph.SincronizarInstagramLoginAsync(_inbound);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        await _auditoria.RegistrarAsync(
            "Integracion",
            0,
            "INSTAGRAM_LOGIN_SYNC",
            null,
            $"Conversaciones: {result.Conversaciones}; mensajes leidos: {result.MensajesLeidos}; importados: {result.MensajesImportados}",
            null);

        return Ok(result);
    }

    [HttpPost("meta/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> RecibirMetaWebhook()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        if (!ValidarFirmaMeta(payload))
        {
            return Unauthorized();
        }

        try
        {
            var procesados = await _metaWebhook.ProcesarAsync(payload);

            _logger.LogInformation(
                "Webhook Meta recibido para Instagram/Facebook. Eventos procesados: {Procesados}.",
                procesados);

            return Ok(new { success = true, received = true, procesados });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando webhook Meta.");
            await _auditoria.RegistrarAsync(
                "Integracion",
                0,
                "WEBHOOK_META_ERROR",
                null,
                "Error interno al procesar el webhook Meta.",
                null);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                received = true,
                error = "Error interno al procesar el webhook."
            });
        }
    }

    [HttpGet("tiktok/webhook")]
    [AllowAnonymous]
    public IActionResult VerificarTikTokWebhook()
    {
        return Ok(new
        {
            success = true,
            message = "Endpoint de verificacion TikTok preparado. Ajustar challenge segun la app de TikTok."
        });
    }

    [HttpPost("tiktok/webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> RecibirTikTokWebhook()
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();

        if (!ValidarFirmaTikTok(payload))
        {
            return Unauthorized();
        }

        _logger.LogInformation("Webhook TikTok recibido. Longitud: {PayloadLength}", payload.Length);
        await _auditoria.RegistrarAsync("Integracion", 0, "WEBHOOK_TIKTOK_RECIBIDO", null, "Pendiente de parser TikTok", null);

        return Ok(new { success = true, received = true });
    }

    private bool ValidarFirmaMeta(string payload)
    {
        var secret = _socialIntegrations.GetConfiguredValue("Meta:AppSecret") ??
            _configuration["Meta:AppSecret"];
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();

        return ValidarHmac(payload, secret, signature, "sha256=");
    }

    private bool ValidarFirmaTikTok(string payload)
    {
        var secret = _socialIntegrations.GetConfiguredValue("TikTok:WebhookSecret") ??
            _configuration["TikTok:WebhookSecret"];
        var signature = Request.Headers["TikTok-Signature"].FirstOrDefault() ??
            Request.Headers["X-Tt-Signature"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        var timestamp = ExtractSignatureValue(signature, "t");
        var signedValue = ExtractSignatureValue(signature, "s") ??
            RemoveSignaturePrefix(signature);
        var payloadToSign = payload;

        if (!string.IsNullOrWhiteSpace(timestamp))
        {
            if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixTimestamp))
            {
                return false;
            }

            var age = Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unixTimestamp);
            if (age > TimeSpan.FromMinutes(5).TotalSeconds)
            {
                return false;
            }

            payloadToSign = $"{timestamp}.{payload}";
        }

        return ValidarHmac(payloadToSign, secret, signedValue, null);
    }

    private static bool ValidarHmac(string payload, string? secret, string? signature, string? requiredPrefix)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(requiredPrefix) &&
            !signature.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var received = RemoveSignaturePrefix(signature);
        if (received.Length != 64 || !received.All(Uri.IsHexDigit))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(received.ToUpperInvariant()));
    }

    private static string RemoveSignaturePrefix(string signature)
    {
        var value = signature.Trim();
        return value.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? value[7..]
            : value;
    }

    private static string? ExtractSignatureValue(string signature, string key)
    {
        return signature
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            .Select(parts => parts[1])
            .FirstOrDefault();
    }

    [HttpPost("{canal}/mensajes/prueba")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> MensajePrueba(string canal, SocialInboundTestDto dto)
    {
        var conversacionId = await _inbound.RegistrarMensajeEntranteAsync(new SocialInboundMessage(
            canal,
            dto.ExternalUserId,
            dto.ContactValue,
            dto.DisplayName,
            dto.Text,
            dto.Type,
            dto.ExternalMessageId));

        return Ok(new
        {
            success = true,
            canal = CanalSocial.Normalizar(canal),
            conversacionId
        });
    }
}
