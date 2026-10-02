using CRM.Data.Models;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;

namespace CRM.Data.Services;

public sealed class SocialIntegrationService
{
    private readonly IConfiguration _configuration;
    private readonly IDataProtector _protector;
    private readonly ILogger<SocialIntegrationService> _logger;
    private readonly string _settingsPath;
    private readonly object _sync = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public SocialIntegrationService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SocialIntegrationService> logger)
    {
        _configuration = configuration;
        _protector = dataProtectionProvider.CreateProtector("CRM.Data.SocialIntegrations.v1");
        _logger = logger;
        _settingsPath = Path.Combine(environment.ContentRootPath, "App_Data", "social-integrations.json");
        Load();
    }

    public IReadOnlyList<SocialChannelStatus> GetChannels(HttpRequest request)
    {
        var baseUrl = $"{request.Scheme}://{request.Host}";

        return
        [
            BuildWhatsApp(baseUrl),
            BuildMetaChannel(
                CanalSocial.Instagram,
                "Instagram",
                "Meta Graph API + Instagram Messaging/Comments",
                $"{baseUrl}/api/integraciones/meta/webhook",
                "Meta:Instagram"),
            BuildMetaChannel(
                CanalSocial.Facebook,
                "Facebook",
                "Meta Graph API + Messenger/Page webhooks",
                $"{baseUrl}/api/integraciones/meta/webhook",
                "Meta:Facebook"),
            BuildTikTok(baseUrl),
            BuildWebsite()
        ];
    }

    public SocialChannelStatus? GetChannel(HttpRequest request, string canal) =>
        GetChannels(request).FirstOrDefault(channel =>
            channel.Canal.Equals(NormalizeIntegrationChannel(canal), StringComparison.OrdinalIgnoreCase));

    public SocialOauthStart BuildOauthStart(HttpRequest request, string canal, string state)
    {
        var normalized = NormalizeIntegrationChannel(canal);
        if (normalized is not (CanalSocial.Instagram or CanalSocial.Facebook or CanalSocial.TikTok))
        {
            return new SocialOauthStart(false, null, ["Canal no soportado."], "Canal no soportado.");
        }

        var requiredKeys = normalized == CanalSocial.TikTok
            ? new[] { "TikTok:ClientKey", "TikTok:ClientSecret" }
            : new[] { "Meta:AppId", "Meta:AppSecret" };
        var missing = requiredKeys
            .Where(key => !HasValue(key))
            .ToArray();

        if (missing.Length > 0)
        {
            return new SocialOauthStart(false, null, missing, "Faltan credenciales para iniciar OAuth.");
        }

        var redirectUri = Uri.EscapeDataString($"{request.Scheme}://{request.Host}/api/integraciones/{normalized.ToLowerInvariant()}/oauth/callback");
        var encodedState = Uri.EscapeDataString(state);
        var authorizationUrl = normalized switch
        {
            CanalSocial.Instagram =>
                $"https://www.instagram.com/oauth/authorize?client_id={GetValue("Meta:AppId")}&redirect_uri={redirectUri}&response_type=code&scope={Uri.EscapeDataString(InstagramScopes)}&state={encodedState}&force_reauth=true",
            CanalSocial.Facebook =>
                $"https://www.facebook.com/{ApiVersion}/dialog/oauth?client_id={GetValue("Meta:AppId")}&redirect_uri={redirectUri}&response_type=code&scope={Uri.EscapeDataString(FacebookScopes)}&state={encodedState}",
            CanalSocial.TikTok =>
                $"https://www.tiktok.com/v2/auth/authorize/?client_key={GetValue("TikTok:ClientKey")}&redirect_uri={redirectUri}&response_type=code&scope={Uri.EscapeDataString(TikTokScopes)}&state={encodedState}",
            _ => null
        };

        return new SocialOauthStart(
            authorizationUrl != null,
            authorizationUrl,
            [],
            authorizationUrl == null
                ? "WhatsApp usa token/webhook, no OAuth desde este panel."
                : "Abre esta URL para autorizar la cuenta.");
    }

    public SocialChannelConfiguration GetConfiguration(string canal, bool revealSecrets = false)
    {
        var normalized = NormalizeIntegrationChannel(canal);
        return new SocialChannelConfiguration(
            normalized,
            GetFields(normalized)
                .Select(field => new SocialConfigField(
                    field.Key,
                    field.Label,
                    field.Secret,
                    field.Required,
                    HasValue(field.Key),
                    field.Secret && !revealSecrets ? Mask(GetValue(field.Key)) : GetValue(field.Key)))
                .ToArray());
    }

    public SocialChannelConfiguration SaveConfiguration(string canal, IReadOnlyDictionary<string, string?> values)
    {
        var normalized = NormalizeIntegrationChannel(canal);
        var allowed = GetFields(normalized).Select(field => field.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            foreach (var item in values)
            {
                if (!allowed.Contains(item.Key)) continue;
                if (item.Value == null) continue;

                var value = item.Value.Trim();
                if (item.Key.EndsWith(":PublicUrl", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(value) &&
                    (!Uri.TryCreate(value, UriKind.Absolute, out var publicUri) ||
                     (publicUri.Scheme != Uri.UriSchemeHttp && publicUri.Scheme != Uri.UriSchemeHttps)))
                {
                    throw new ArgumentException("La URL pública debe comenzar con http:// o https://.");
                }
                if (item.Key.Equals("WhatsApp:Numbers", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    try
                    {
                        var numbers = JsonSerializer.Deserialize<List<WhatsAppNumber>>(value,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (numbers == null || numbers.Count == 0 ||
                            numbers.Any(n => string.IsNullOrWhiteSpace(n.PhoneNumberId) ||
                                !n.PhoneNumberId.All(char.IsDigit)) ||
                            numbers.Select(n => n.PhoneNumberId).Distinct(StringComparer.Ordinal).Count() != numbers.Count)
                            throw new ArgumentException("La lista de numeros debe contener Phone Number IDs numericos y unicos.");
                    }
                    catch (JsonException)
                    {
                        throw new ArgumentException("WhatsApp:Numbers debe ser una lista JSON valida.");
                    }
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    _values.Remove(item.Key);
                }
                else if (value.Contains('*') && HasValue(item.Key))
                {
                    continue;
                }
                else
                {
                    _values[item.Key] = value;
                }
            }

            Save();
        }

        return GetConfiguration(normalized);
    }

    public string? GetConfiguredValue(string key) => GetValue(key);

    private string ApiVersion => GetValue("Meta:ApiVersion") ?? "v25.0";

    private const string FacebookScopes =
        "pages_show_list,pages_messaging,pages_read_engagement,pages_read_user_content,pages_manage_engagement,pages_manage_posts,pages_manage_metadata,read_insights";

    private const string InstagramScopes =
        "instagram_business_basic,instagram_business_manage_messages,instagram_business_manage_comments,instagram_business_content_publish";

    private const string TikTokScopes =
        "user.info.basic,business.basic,video.list,comment.list";

    private SocialChannelStatus BuildWhatsApp(string baseUrl)
    {
        var required = new[]
        {
            Required("WhatsApp:WebhookVerifyToken"),
            Required("WhatsApp:AccessToken"),
            new SocialRequiredConfig("WhatsApp:Numbers o WhatsApp:PhoneNumberId",
                HasValue("WhatsApp:Numbers") || HasValue("WhatsApp:PhoneNumberId")),
            Required("WhatsApp:BusinessAccountId")
        };

        return new SocialChannelStatus(
            CanalSocial.WhatsApp,
            "WhatsApp",
            "Meta WhatsApp Cloud API",
            required.All(item => item.Configured),
            $"{baseUrl}/api/whatsapp/webhook",
            null,
            required,
            ["graph.facebook.com", "lookaside.fbsbx.com", "*.r2.cloudflarestorage.com"],
            GetValue("WhatsApp:PublicUrl"));
    }

    private SocialChannelStatus BuildMetaChannel(
        string canal,
        string nombre,
        string provider,
        string webhookUrl,
        string prefix)
    {
        var required = canal == CanalSocial.Instagram
            ? new[]
            {
                Required("Meta:AppId"),
                Required("Meta:AppSecret"),
                Required("Meta:WebhookVerifyToken"),
                Required("Meta:Instagram:LoginUserId"),
                Required("Meta:Instagram:LoginAccessToken")
            }
            : new[]
            {
                Required("Meta:AppId"),
                Required("Meta:AppSecret"),
                Required("Meta:WebhookVerifyToken"),
                Required($"{prefix}:PageId"),
                Required($"{prefix}:AccessToken")
            };

        return new SocialChannelStatus(
            canal,
            nombre,
            provider,
            required.All(item => item.Configured),
            webhookUrl,
            $"/api/integraciones/{canal.ToLowerInvariant()}/oauth/start",
            required,
            canal == CanalSocial.Instagram
                ? ["graph.instagram.com", "graph.facebook.com"]
                : ["graph.facebook.com", "www.facebook.com"],
            GetValue($"{prefix}:PublicUrl"));
    }

    private SocialChannelStatus BuildTikTok(string baseUrl)
    {
        var required = new[]
        {
            Required("TikTok:ClientKey"),
            Required("TikTok:ClientSecret"),
            Required("TikTok:WebhookSecret"),
            Required("TikTok:AdvertiserId"),
            Required("TikTok:AccessToken")
        };

        return new SocialChannelStatus(
            CanalSocial.TikTok,
            "TikTok",
            "TikTok Developers / Business API",
            required.All(item => item.Configured),
            $"{baseUrl}/api/integraciones/tiktok/webhook",
            "/api/integraciones/tiktok/oauth/start",
            required,
            ["open.tiktokapis.com", "business-api.tiktok.com", "www.tiktok.com"],
            GetValue("TikTok:PublicUrl"));
    }

    private SocialChannelStatus BuildWebsite()
    {
        var url = GetValue("Website:PublicUrl");
        return new SocialChannelStatus(
            "WEBSITE",
            "Página web",
            "Sitio web corporativo",
            !string.IsNullOrWhiteSpace(url),
            "",
            null,
            [new SocialRequiredConfig("Website:PublicUrl", !string.IsNullOrWhiteSpace(url))],
            [],
            url);
    }

    private SocialRequiredConfig Required(string key) =>
        new(key, HasValue(key));

    private string? GetValue(string key) =>
        _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : _configuration[key] ?? GetDefaultValue(key);

    private static string NormalizeIntegrationChannel(string? canal) =>
        string.Equals(canal?.Trim(), "WEBSITE", StringComparison.OrdinalIgnoreCase)
            ? "WEBSITE"
            : string.Equals(canal?.Trim(), "R2", StringComparison.OrdinalIgnoreCase)
                ? "R2"
                : CanalSocial.Normalizar(canal);

    private static string? GetDefaultValue(string key) => key switch
    {
        "Meta:Facebook:PublicUrl" => "https://www.facebook.com/profile.php?id=61594244270955&locale=es_LA",
        "Meta:Instagram:PublicUrl" => "https://www.instagram.com/hpdglassgroupoficial/",
        "Website:PublicUrl" => "https://www.hpdglass.com/",
        _ => null
    };

    private bool HasValue(string key) => !string.IsNullOrWhiteSpace(GetValue(key));

    private void Load()
    {
        if (!File.Exists(_settingsPath)) return;

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var protectedFile = JsonSerializer.Deserialize<ProtectedSocialIntegrationSettingsFile>(json, _jsonOptions);

            if (protectedFile?.Version == 1 && !string.IsNullOrWhiteSpace(protectedFile.ProtectedPayload))
            {
                var protectedJson = _protector.Unprotect(protectedFile.ProtectedPayload);
                var protectedValues = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    protectedJson,
                    _jsonOptions);
                _values = protectedValues == null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(protectedValues, StringComparer.OrdinalIgnoreCase);
                return;
            }

            // Compatibilidad con instalaciones anteriores. Al volver a guardar
            // desde Conexiones, el archivo se reemplaza por el formato cifrado.
            var legacy = JsonSerializer.Deserialize<SocialIntegrationSettingsFile>(json, _jsonOptions);
            _values = legacy?.Values == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(legacy.Values, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo descifrar la configuracion de integraciones.");
            _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var payload = JsonSerializer.Serialize(_values, _jsonOptions);
        var protectedFile = new ProtectedSocialIntegrationSettingsFile
        {
            ProtectedPayload = _protector.Protect(payload)
        };
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(protectedFile, _jsonOptions));
        File.Move(temporaryPath, _settingsPath, true);
    }

    private static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= 8
            ? new string('*', value.Length)
            : $"{value[..4]}{new string('*', Math.Min(12, value.Length - 8))}{value[^4..]}";
    }

    private static IReadOnlyList<SocialConfigFieldDefinition> GetFields(string canal) =>
        NormalizeIntegrationChannel(canal) switch
        {
            "R2" =>
            [
                new("R2:AccountId", "Cloudflare Account ID", false, true),
                new("R2:AccessKeyId", "R2 Access Key ID", true, true),
                new("R2:SecretAccessKey", "R2 Secret Access Key", true, true),
                new("R2:BucketName", "Nombre del bucket", false, true)
            ],
            CanalSocial.Instagram =>
            [
                new("Meta:Instagram:PublicUrl", "URL", false, false),
                new("Meta:AppId", "Meta App ID", false, true),
                new("Meta:AppSecret", "Meta App Secret", true, true),
                new("Meta:WebhookVerifyToken", "Meta Webhook Verify Token", true, true),
                new("Meta:ApiVersion", "Meta API Version", false, false),
                new("Meta:Instagram:LoginUserId", "Instagram Login User ID", false, true),
                new("Meta:Instagram:LoginAccessToken", "Instagram Login Access Token", true, true),
                new("Meta:Instagram:PageId", "Facebook Page ID vinculada (flujo anterior)", false, false),
                new("Meta:Instagram:InstagramBusinessAccountId", "Instagram Professional User ID", false, false),
                new("Meta:Instagram:AccessToken", "Instagram/Page Access Token (flujo anterior)", true, false)
            ],
            CanalSocial.Facebook =>
            [
                new("Meta:Facebook:PublicUrl", "URL", false, false),
                new("Meta:AppId", "Meta App ID", false, true),
                new("Meta:AppSecret", "Meta App Secret", true, true),
                new("Meta:WebhookVerifyToken", "Meta Webhook Verify Token", true, true),
                new("Meta:ApiVersion", "Meta API Version", false, false),
                new("Meta:Facebook:PageId", "Facebook Page ID", false, true),
                new("Meta:Facebook:AccessToken", "Page Access Token", true, true)
            ],
            CanalSocial.TikTok =>
            [
                new("TikTok:PublicUrl", "URL", false, false),
                new("TikTok:ClientKey", "TikTok Client Key", false, true),
                new("TikTok:ClientSecret", "TikTok Client Secret", true, true),
                new("TikTok:WebhookSecret", "TikTok Webhook Secret", true, true),
                new("TikTok:AdvertiserId", "TikTok Advertiser ID", false, true),
                new("TikTok:AccessToken", "Business API Access Token", true, true),
                new("TikTok:DisplayAccessToken", "Display API Access Token (video.list)", true, false),
                new("TikTok:RefreshToken", "OAuth Refresh Token", true, false),
                new("TikTok:OpenId", "TikTok Open ID", false, false),
                new("TikTok:AccessTokenExpiresAtUtc", "Vencimiento Access Token (UTC)", false, false),
                new("TikTok:RefreshTokenExpiresAtUtc", "Vencimiento Refresh Token (UTC)", false, false)
            ],
            "WEBSITE" =>
            [
                new("Website:PublicUrl", "URL", false, true)
            ],
            _ =>
            [
                new("WhatsApp:PublicUrl", "URL", false, false),
                new("WhatsApp:WebhookVerifyToken", "Webhook Verify Token", true, true),
                new("WhatsApp:AppSecret", "Meta App Secret para firma del webhook", true, true),
                new("WhatsApp:AccessToken", "Access Token", true, true),
                new("WhatsApp:PhoneNumberId", "Phone Number ID", false, true),
                new("WhatsApp:Numbers", "Numeros del WABA (JSON: [{\"phoneNumberId\":\"ID\",\"displayNumber\":\"+51...\"}])", false, false),
                new("WhatsApp:BusinessAccountId", "Business Account ID", false, true),
                new("WhatsApp:ApiVersion", "API Version", false, false),
                new("WhatsApp:SendMessagesToMeta", "Enviar mensajes a Meta true/false", false, false)
            ]
        };
}
