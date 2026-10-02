using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CRM.Data.Models;

namespace CRM.Data.Services;

public sealed class MetaMessagingService
{
    private readonly HttpClient _httpClient;
    private readonly SocialIntegrationService _socialIntegrations;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MetaMessagingService> _logger;

    public MetaMessagingService(
        HttpClient httpClient,
        SocialIntegrationService socialIntegrations,
        IConfiguration configuration,
        ILogger<MetaMessagingService> logger)
    {
        _httpClient = httpClient;
        _socialIntegrations = socialIntegrations;
        _configuration = configuration;
        _logger = logger;
    }

    public bool PuedeEnviar(string canal)
    {
        var normalized = CanalSocial.Normalizar(canal);
        if (normalized is not (CanalSocial.Facebook or CanalSocial.Instagram))
        {
            return false;
        }

        if (normalized == CanalSocial.Instagram)
        {
            return !string.IsNullOrWhiteSpace(GetInstagramAccountId()) &&
                !string.IsNullOrWhiteSpace(GetInstagramLoginToken());
        }

        return !string.IsNullOrWhiteSpace(GetPageId(normalized)) &&
            !string.IsNullOrWhiteSpace(GetAccessToken(normalized));
    }

    public async Task<string?> SendTextMessageAsync(string canal, string recipientId, string text)
    {
        var normalized = CanalSocial.Normalizar(canal);
        if (normalized == CanalSocial.Instagram)
        {
            return await SendInstagramTextMessageAsync(recipientId, text);
        }

        var pageId = GetPageId(normalized);
        var configuredToken = GetAccessToken(normalized);

        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(configuredToken))
        {
            throw new InvalidOperationException($"Faltan Page ID o Access Token para {normalized}.");
        }

        if (string.IsNullOrWhiteSpace(recipientId))
        {
            throw new InvalidOperationException("No hay identificador externo del cliente para responder.");
        }

        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v25.0";
        var accessToken = await ResolvePageAccessTokenAsync(apiVersion, pageId, configuredToken);
        var url = $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(pageId)}/messages";
        var payload = new
        {
            recipient = new { id = recipientId },
            messaging_type = "RESPONSE",
            message = new { text }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Meta Messaging devolvio HTTP {StatusCode} para {Canal}: {Body}",
                (int)response.StatusCode,
                normalized,
                body);
            throw new InvalidOperationException(BuildMetaMessagingError((int)response.StatusCode, body));
        }

        return ReadMessageId(body);
    }

    public async Task<string?> ReplyToPublicCommentAsync(string canal, string commentId, string text)
    {
        var normalized = CanalSocial.Normalizar(canal);
        if (normalized is not (CanalSocial.Facebook or CanalSocial.Instagram))
        {
            throw new InvalidOperationException("Solo Facebook e Instagram admiten respuestas públicas desde Marketing.");
        }

        if (string.IsNullOrWhiteSpace(commentId) || string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("El comentario y la respuesta son obligatorios.");
        }

        var apiVersion = GetValue("Meta:ApiVersion") ?? _configuration["Meta:ApiVersion"] ?? "v26.0";

        if (normalized == CanalSocial.Instagram)
        {
            return await ReplyToInstagramPublicCommentAsync(apiVersion, commentId, text);
        }

        var pageId = GetPageId(normalized) ??
            throw new InvalidOperationException("Falta el Page ID de Facebook.");
        var configuredToken = GetAccessToken(normalized) ??
            throw new InvalidOperationException("Falta el Access Token de Facebook.");
        var accessToken = await ResolvePageAccessTokenAsync(apiVersion, pageId, configuredToken);
        var url = $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(commentId)}/comments";

        return await PostPublicCommentReplyAsync(normalized, url, accessToken, text);
    }

    private async Task<string?> ReplyToInstagramPublicCommentAsync(
        string apiVersion,
        string commentId,
        string text)
    {
        var errors = new List<string>();
        var instagramLoginToken = GetValue("Meta:Instagram:LoginAccessToken");
        if (!string.IsNullOrWhiteSpace(instagramLoginToken))
        {
            var instagramUrl = $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(commentId)}/replies";
            try
            {
                return await PostPublicCommentReplyAsync(CanalSocial.Instagram, instagramUrl, instagramLoginToken, text);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"Instagram Login: {ex.Message}");
            }
        }

        var pageId = GetValue("Meta:Instagram:PageId") ?? GetValue("Meta:Facebook:PageId");
        var facebookLoginCandidates = new[]
        {
            ("Instagram/Page Token", GetValue("Meta:Instagram:AccessToken")),
            ("Facebook Page Token", GetValue("Meta:Facebook:AccessToken"))
        };

        foreach (var (name, configuredToken) in facebookLoginCandidates)
        {
            if (string.IsNullOrWhiteSpace(configuredToken)) continue;

            try
            {
                var accessToken = !string.IsNullOrWhiteSpace(pageId)
                    ? await ResolvePageAccessTokenAsync(apiVersion, pageId, configuredToken)
                    : configuredToken;
                var facebookUrl = $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(commentId)}/replies";
                return await PostPublicCommentReplyAsync(CanalSocial.Instagram, facebookUrl, accessToken, text);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"{name}: {ex.Message}");
            }
        }

        var detail = errors.Count > 0 ? " " + string.Join(" | ", errors) : string.Empty;
        throw new InvalidOperationException(
            "Instagram no autorizó responder el comentario. Reconecta la cuenta con instagram_business_manage_comments o configura un Page Token con instagram_manage_comments." +
            detail);
    }

    private async Task<string?> PostPublicCommentReplyAsync(
        string canal,
        string url,
        string accessToken,
        string text)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["message"] = text.Trim()
        });

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Meta devolvio HTTP {StatusCode} al responder un comentario de {Canal}: {Body}",
                (int)response.StatusCode,
                canal,
                body);
            throw new InvalidOperationException(BuildMetaMessagingError((int)response.StatusCode, body));
        }

        return ReadCreatedObjectId(body);
    }

    private async Task<string?> SendInstagramTextMessageAsync(string recipientId, string text)
    {
        var accountId = GetInstagramAccountId();
        var accessToken = GetInstagramLoginToken();
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "Faltan Instagram Professional User ID o Instagram Login Access Token.");
        }

        if (string.IsNullOrWhiteSpace(recipientId))
        {
            throw new InvalidOperationException("No hay identificador externo del cliente para responder.");
        }

        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v26.0";
        var url = $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(accountId)}/messages";
        var payload = new
        {
            recipient = new { id = recipientId },
            message = new { text }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Instagram Send API devolvio HTTP {StatusCode}: {Body}",
                (int)response.StatusCode,
                body);
            throw new InvalidOperationException(BuildMetaMessagingError((int)response.StatusCode, body));
        }

        return ReadMessageId(body);
    }

    private static string BuildMetaMessagingError(int statusCode, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeNode) ? codeNode.ToString() : null;
                var subcode = error.TryGetProperty("error_subcode", out var subcodeNode) ? subcodeNode.ToString() : null;
                var message = error.TryGetProperty("message", out var messageNode)
                    ? messageNode.GetString()
                    : null;

                if (code == "10" && subcode == "2018278")
                {
                    return "Facebook rechazo el mensaje por la politica de Messenger: la conversacion esta fuera de la ventana de 24 horas. El cliente debe escribir nuevamente o se debe usar una modalidad de mensaje permitida por Meta. No es un error del token. Codigo 10, subcodigo 2018278.";
                }

                if (!string.IsNullOrWhiteSpace(message))
                {
                    return $"Meta rechazo el mensaje. HTTP {statusCode}: {message} (codigo {code ?? "desconocido"}, subcodigo {subcode ?? "desconocido"}).";
                }
            }
        }
        catch (JsonException)
        {
            // Conserva un error util si Meta devuelve una respuesta no JSON.
        }

        return $"Meta rechazo el mensaje. HTTP {statusCode}: {body}";
    }

    private async Task<string> ResolvePageAccessTokenAsync(
        string apiVersion,
        string pageId,
        string configuredToken)
    {
        var meUrl =
            $"https://graph.facebook.com/{apiVersion}/me" +
            $"?fields=id,name&access_token={Uri.EscapeDataString(configuredToken)}";

        try
        {
            using var meResponse = await _httpClient.GetAsync(meUrl);
            var meBody = await meResponse.Content.ReadAsStringAsync();
            if (meResponse.IsSuccessStatusCode)
            {
                using var meDocument = JsonDocument.Parse(meBody);
                var meId = meDocument.RootElement.TryGetProperty("id", out var idNode)
                    ? idNode.GetString()
                    : null;

                if (pageId.Equals(meId, StringComparison.OrdinalIgnoreCase))
                {
                    return configuredToken;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo validar si el token de Meta pertenece a la pagina.");
        }

        var accountsUrl =
            $"https://graph.facebook.com/{apiVersion}/me/accounts" +
            $"?fields=id,name,access_token&access_token={Uri.EscapeDataString(configuredToken)}";

        using var accountsResponse = await _httpClient.GetAsync(accountsUrl);
        var accountsBody = await accountsResponse.Content.ReadAsStringAsync();
        if (!accountsResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"El token no permite obtener las paginas administradas. HTTP {(int)accountsResponse.StatusCode}: {accountsBody}");
        }

        using var accountsDocument = JsonDocument.Parse(accountsBody);
        if (!accountsDocument.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Meta no devolvio paginas administradas para obtener Page Access Token.");
        }

        foreach (var page in data.EnumerateArray())
        {
            var id = page.TryGetProperty("id", out var idNode)
                ? idNode.GetString()
                : null;
            if (!pageId.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pageToken = page.TryGetProperty("access_token", out var tokenNode)
                ? tokenNode.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                return pageToken;
            }
        }

        throw new InvalidOperationException(
            $"El token configurado no contiene acceso a la pagina {pageId}. Genera un Page Access Token de esa pagina.");
    }

    private string? GetPageId(string canal) =>
        canal == CanalSocial.Instagram
            ? GetValue("Meta:Instagram:PageId")
            : GetValue("Meta:Facebook:PageId");

    private string? GetAccessToken(string canal) =>
        canal == CanalSocial.Instagram
            ? GetValue("Meta:Instagram:AccessToken")
            : GetValue("Meta:Facebook:AccessToken");

    private string? GetInstagramAccountId() =>
        GetValue("Meta:Instagram:InstagramBusinessAccountId") ??
        GetValue("Meta:Instagram:LoginUserId");

    private string? GetInstagramLoginToken() =>
        GetValue("Meta:Instagram:LoginAccessToken") ??
        GetValue("Meta:Instagram:AccessToken");

    private string? GetValue(string key) =>
        _socialIntegrations.GetConfiguredValue(key) ?? _configuration[key];

    private static string? ReadMessageId(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("message_id", out var messageId)
            ? messageId.GetString()
            : null;
    }

    private static string? ReadCreatedObjectId(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("id", out var id)) return id.GetString();
        return document.RootElement.TryGetProperty("message_id", out var messageId)
            ? messageId.GetString()
            : null;
    }
}
