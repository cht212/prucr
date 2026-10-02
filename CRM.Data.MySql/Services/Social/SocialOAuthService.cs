using CRM.Data.Models;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace CRM.Data.Services;

public sealed class SocialOAuthService
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly HttpClient _http;
    private readonly SocialIntegrationService _settings;
    private readonly IDataProtector _stateProtector;
    private readonly ILogger<SocialOAuthService> _logger;

    public SocialOAuthService(
        HttpClient http,
        SocialIntegrationService settings,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SocialOAuthService> logger)
    {
        _http = http;
        _settings = settings;
        _stateProtector = dataProtectionProvider.CreateProtector("CRM.Data.SocialOAuth.State.v1");
        _logger = logger;
    }

    public SocialOauthStart CreateStart(HttpRequest request, string canal, string userId)
    {
        var normalized = NormalizeSupportedChannel(canal);
        var redirectUri = BuildRedirectUri(request, normalized);
        var statePayload = JsonSerializer.Serialize(new OAuthState(
            normalized,
            userId,
            redirectUri,
            DateTimeOffset.UtcNow,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16))));
        var state = _stateProtector.Protect(statePayload);
        return _settings.BuildOauthStart(request, normalized, state);
    }

    public async Task<SocialOauthExchangeResult> ExchangeAsync(
        HttpRequest request,
        string canal,
        string userId,
        string code,
        string state,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeSupportedChannel(canal);
        var oauthState = ReadAndValidateState(state, normalized, userId);
        var expectedRedirect = BuildRedirectUri(request, normalized);
        if (!Uri.TryCreate(oauthState.RedirectUri, UriKind.Absolute, out var stateUri) ||
            !Uri.TryCreate(expectedRedirect, UriKind.Absolute, out var expectedUri) ||
            !Uri.Compare(stateUri, expectedUri, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase).Equals(0))
        {
            throw new InvalidOperationException("La URL de retorno OAuth no coincide con la solicitud inicial.");
        }

        return normalized switch
        {
            CanalSocial.Instagram => await ExchangeInstagramAsync(code, expectedRedirect, cancellationToken),
            CanalSocial.Facebook => await ExchangeFacebookAsync(code, expectedRedirect, cancellationToken),
            CanalSocial.TikTok => await ExchangeTikTokAsync(code, expectedRedirect, cancellationToken),
            _ => throw new InvalidOperationException("Canal OAuth no soportado.")
        };
    }

    public async Task<bool> RefreshTikTokIfNeededAsync(CancellationToken cancellationToken = default)
    {
        var refreshToken = _settings.GetConfiguredValue("TikTok:RefreshToken");
        var expiresValue = _settings.GetConfiguredValue("TikTok:AccessTokenExpiresAtUtc");
        if (string.IsNullOrWhiteSpace(refreshToken) ||
            !DateTimeOffset.TryParse(expiresValue, out var expiresAt) ||
            expiresAt > DateTimeOffset.UtcNow.AddMinutes(10))
            return false;

        using var response = await _http.PostAsync(
            "https://open.tiktokapis.com/v2/oauth/token/",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_key"] = Required("TikTok:ClientKey"),
                ["client_secret"] = Required("TikTok:ClientSecret"),
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            }), cancellationToken);
        using var token = await ReadSuccessJsonAsync(response, cancellationToken);
        var root = token.RootElement;
        var newAccessToken = GetRequiredString(root, "access_token");
        var newRefreshToken = GetRequiredString(root, "refresh_token");
        var now = DateTimeOffset.UtcNow;
        var accessSeconds = GetOptionalInt64(root, "expires_in");
        var refreshSeconds = GetOptionalInt64(root, "refresh_expires_in");

        _settings.SaveConfiguration(CanalSocial.TikTok, new Dictionary<string, string?>
        {
            ["TikTok:DisplayAccessToken"] = newAccessToken,
            ["TikTok:RefreshToken"] = newRefreshToken,
            ["TikTok:OpenId"] = GetOptionalString(root, "open_id"),
            ["TikTok:AccessTokenExpiresAtUtc"] = accessSeconds.HasValue ? now.AddSeconds(accessSeconds.Value).ToString("O") : null,
            ["TikTok:RefreshTokenExpiresAtUtc"] = refreshSeconds.HasValue ? now.AddSeconds(refreshSeconds.Value).ToString("O") : null
        });
        return true;
    }

    private OAuthState ReadAndValidateState(string state, string channel, string userId)
    {
        if (string.IsNullOrWhiteSpace(state))
            throw new InvalidOperationException("Falta el estado de seguridad OAuth.");

        OAuthState payload;
        try
        {
            payload = JsonSerializer.Deserialize<OAuthState>(_stateProtector.Unprotect(state))
                ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("El estado de seguridad OAuth no es válido.");
        }

        if (!payload.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase) ||
            !payload.UserId.Equals(userId, StringComparison.Ordinal))
            throw new InvalidOperationException("El estado OAuth no pertenece a esta sesión.");
        if (payload.IssuedAtUtc > DateTimeOffset.UtcNow.AddMinutes(1) ||
            DateTimeOffset.UtcNow - payload.IssuedAtUtc > StateLifetime)
            throw new InvalidOperationException("La autorización OAuth venció. Iníciala nuevamente.");

        return payload;
    }

    private async Task<SocialOauthExchangeResult> ExchangeInstagramAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var appId = Required("Meta:AppId");
        var appSecret = Required("Meta:AppSecret");
        using var response = await _http.PostAsync(
            "https://api.instagram.com/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = appId,
                ["client_secret"] = appSecret,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
                ["code"] = code
            }), cancellationToken);
        using var shortToken = await ReadSuccessJsonAsync(response, cancellationToken);
        var accessToken = GetRequiredString(shortToken.RootElement, "access_token");
        var userId = GetRequiredString(shortToken.RootElement, "user_id");

        var longTokenUrl = "https://graph.instagram.com/access_token" +
            $"?grant_type=ig_exchange_token&client_secret={Uri.EscapeDataString(appSecret)}" +
            $"&access_token={Uri.EscapeDataString(accessToken)}";
        using var longResponse = await _http.GetAsync(longTokenUrl, cancellationToken);
        using var longToken = await ReadSuccessJsonAsync(longResponse, cancellationToken);
        var longAccessToken = GetRequiredString(longToken.RootElement, "access_token");

        _settings.SaveConfiguration(CanalSocial.Instagram, new Dictionary<string, string?>
        {
            ["Meta:Instagram:LoginUserId"] = userId,
            ["Meta:Instagram:LoginAccessToken"] = longAccessToken
        });
        return new(true, CanalSocial.Instagram,
            "Instagram quedó conectado y el token se guardó cifrado.",
            ["Meta:Instagram:LoginUserId", "Meta:Instagram:LoginAccessToken"]);
    }

    private async Task<SocialOauthExchangeResult> ExchangeFacebookAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var appId = Required("Meta:AppId");
        var appSecret = Required("Meta:AppSecret");
        var version = _settings.GetConfiguredValue("Meta:ApiVersion") ?? "v25.0";
        var tokenUrl = $"https://graph.facebook.com/{version}/oauth/access_token" +
            $"?client_id={Uri.EscapeDataString(appId)}&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&client_secret={Uri.EscapeDataString(appSecret)}&code={Uri.EscapeDataString(code)}";
        using var response = await _http.GetAsync(tokenUrl, cancellationToken);
        using var token = await ReadSuccessJsonAsync(response, cancellationToken);
        var shortUserToken = GetRequiredString(token.RootElement, "access_token");

        var longTokenUrl = $"https://graph.facebook.com/{version}/oauth/access_token" +
            $"?grant_type=fb_exchange_token&client_id={Uri.EscapeDataString(appId)}" +
            $"&client_secret={Uri.EscapeDataString(appSecret)}" +
            $"&fb_exchange_token={Uri.EscapeDataString(shortUserToken)}";
        using var longResponse = await _http.GetAsync(longTokenUrl, cancellationToken);
        using var longToken = await ReadSuccessJsonAsync(longResponse, cancellationToken);
        var userToken = GetRequiredString(longToken.RootElement, "access_token");

        var pagesUrl = $"https://graph.facebook.com/{version}/me/accounts" +
            $"?fields=id,name,access_token&limit=100&access_token={Uri.EscapeDataString(userToken)}";
        using var pagesResponse = await _http.GetAsync(pagesUrl, cancellationToken);
        using var pagesJson = await ReadSuccessJsonAsync(pagesResponse, cancellationToken);
        var pages = pagesJson.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().ToArray()
            : [];
        if (pages.Length == 0)
            throw new InvalidOperationException("Meta no devolvió ninguna página administrada por esta cuenta.");

        var configuredPageId = _settings.GetConfiguredValue("Meta:Facebook:PageId");
        var selected = !string.IsNullOrWhiteSpace(configuredPageId)
            ? pages.FirstOrDefault(page => GetOptionalString(page, "id") == configuredPageId)
            : pages[0];
        if (selected.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("La página configurada no está disponible para la cuenta autorizada.");
        if (pages.Length > 1 && string.IsNullOrWhiteSpace(configuredPageId))
            _logger.LogInformation("OAuth de Facebook encontró {PageCount} páginas; se usará la primera.", pages.Length);

        var pageId = GetRequiredString(selected, "id");
        var pageToken = GetRequiredString(selected, "access_token");
        _settings.SaveConfiguration(CanalSocial.Facebook, new Dictionary<string, string?>
        {
            ["Meta:Facebook:PageId"] = pageId,
            ["Meta:Facebook:AccessToken"] = pageToken
        });
        return new(true, CanalSocial.Facebook,
            "Facebook quedó conectado y el token de la página se guardó cifrado.",
            ["Meta:Facebook:PageId", "Meta:Facebook:AccessToken"]);
    }

    private async Task<SocialOauthExchangeResult> ExchangeTikTokAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync(
            "https://open.tiktokapis.com/v2/oauth/token/",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_key"] = Required("TikTok:ClientKey"),
                ["client_secret"] = Required("TikTok:ClientSecret"),
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            }), cancellationToken);
        using var token = await ReadSuccessJsonAsync(response, cancellationToken);
        var root = token.RootElement;
        var accessToken = GetRequiredString(root, "access_token");
        var refreshToken = GetRequiredString(root, "refresh_token");
        var openId = GetRequiredString(root, "open_id");
        var accessSeconds = GetOptionalInt64(root, "expires_in");
        var refreshSeconds = GetOptionalInt64(root, "refresh_expires_in");
        var now = DateTimeOffset.UtcNow;

        _settings.SaveConfiguration(CanalSocial.TikTok, new Dictionary<string, string?>
        {
            ["TikTok:DisplayAccessToken"] = accessToken,
            ["TikTok:RefreshToken"] = refreshToken,
            ["TikTok:OpenId"] = openId,
            ["TikTok:AccessTokenExpiresAtUtc"] = accessSeconds.HasValue ? now.AddSeconds(accessSeconds.Value).ToString("O") : null,
            ["TikTok:RefreshTokenExpiresAtUtc"] = refreshSeconds.HasValue ? now.AddSeconds(refreshSeconds.Value).ToString("O") : null
        });
        return new(true, CanalSocial.TikTok,
            "TikTok quedó autorizado y sus tokens se guardaron cifrados.",
            ["TikTok:DisplayAccessToken", "TikTok:RefreshToken", "TikTok:OpenId"]);
    }

    private static async Task<JsonDocument> ReadSuccessJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonDocument? json = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(payload)) json = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            // Algunos proveedores devuelven HTML o texto plano en errores de proxy.
        }
        if (response.IsSuccessStatusCode && json != null) return json;

        var detail = json == null ? null :
            GetOptionalString(json.RootElement, "error_description") ??
            (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                ? GetOptionalString(error, "message")
                : GetOptionalString(json.RootElement, "error"));
        json?.Dispose();
        if (response.IsSuccessStatusCode)
            throw new InvalidOperationException("El proveedor OAuth devolvió una respuesta inválida.");
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"El proveedor OAuth respondió con estado {(int)response.StatusCode}."
            : $"El proveedor OAuth rechazó la solicitud: {detail}");
    }

    private string Required(string key) =>
        _settings.GetConfiguredValue(key) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Falta configurar {key}.");

    private static string GetRequiredString(JsonElement element, string name) =>
        GetOptionalString(element, name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"El proveedor OAuth no devolvió {name}.");

    private static string? GetOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static long? GetOptionalInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed) ? parsed : null;

    private static string BuildRedirectUri(HttpRequest request, string channel) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}/api/integraciones/{channel.ToLowerInvariant()}/oauth/callback";

    private static string NormalizeSupportedChannel(string canal)
    {
        var normalized = CanalSocial.Normalizar(canal);
        return normalized is CanalSocial.Instagram or CanalSocial.Facebook or CanalSocial.TikTok
            ? normalized
            : throw new InvalidOperationException("Canal OAuth no soportado.");
    }

    private sealed record OAuthState(
        string Channel,
        string UserId,
        string RedirectUri,
        DateTimeOffset IssuedAtUtc,
        string Nonce);
}
