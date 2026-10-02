using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CRM.Data.Services;

public sealed class WhatsAppCloudApiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly SocialIntegrationService _socialIntegrations;
    private readonly ILogger<WhatsAppCloudApiService> _logger;
    private readonly WhatsAppNumberRegistry _numbers;

    public WhatsAppCloudApiService(
        HttpClient httpClient,
        IConfiguration configuration,
        SocialIntegrationService socialIntegrations,
        WhatsAppNumberRegistry numbers,
        ILogger<WhatsAppCloudApiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _socialIntegrations = socialIntegrations;
        _logger = logger;
        _numbers = numbers;
    }

    public bool ShouldSendToMeta =>
        bool.TryParse(GetValue("WhatsApp:SendMessagesToMeta"), out var enabled) &&
        enabled &&
        IsConfigured;

    private bool IsConfigured =>
        !string.IsNullOrWhiteSpace(GetValue("WhatsApp:AccessToken")) &&
        _numbers.DefaultPhoneNumberId != null;

    // =========================================================
    // INDICADOR DE "ESCRIBIENDO..." (typing indicator)
    // =========================================================
    //
    // Marca como leído el último mensaje entrante del cliente y le
    // muestra el indicador de "escribiendo..." en su chat de
    // WhatsApp. Meta lo apaga solo a los 25 segundos, o antes si le
    // enviamos la respuesta real. No existe un endpoint para
    // "apagarlo" manualmente: el propio envío del mensaje lo cierra.
    //
    // Requiere el whatsappId (message.id) del ÚLTIMO mensaje que
    // el cliente nos envió.
    //
    // =========================================================

    public async Task<bool> MostrarEscribiendoAsync(
        string ultimoMensajeEntranteWhatsappId,
        string? originPhoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ultimoMensajeEntranteWhatsappId))
        {
            return false;
        }

        var accessToken = Required("WhatsApp:AccessToken");
        var phoneNumberId = ResolvePhoneNumberId(originPhoneNumberId);
        var apiVersion = GetValue("WhatsApp:ApiVersion");
        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            apiVersion = "v25.0";
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            status = "read",
            message_id = ultimoMensajeEntranteWhatsappId,
            typing_indicator = new { type = "text" }
        };

        var endpoint = $"https://graph.facebook.com/{apiVersion}/{phoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "No se pudo mostrar el indicador de 'escribiendo' al cliente. HTTP {StatusCode}: {ResponseBody}",
                (int)response.StatusCode,
                responseBody);
            return false;
        }

        return true;
    }

    public Task<string?> SendTextMessageAsync(
        string to,
        string text,
        string? originPhoneNumberId = null,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = to,
            ["type"] = "text",
            ["text"] = new
            {
                preview_url = false,
                body = text
            }
        };
        if (!string.IsNullOrWhiteSpace(replyToMessageId))
        {
            payload["context"] = new { message_id = replyToMessageId };
        }

        return SendMessageAsync(payload, originPhoneNumberId, cancellationToken);
    }

    public async Task<WhatsAppMediaDownload> DownloadMediaAsync(
        string? mediaId,
        string? originPhoneNumberId = null,
        string? webhookMediaUrl = null,
        string? mimeTypeHint = null,
        CancellationToken cancellationToken = default)
    {
        var accessToken = Required("WhatsApp:AccessToken");
        var downloadUrl = TryGetMetaMediaUrl(webhookMediaUrl);
        var metadataContentType = mimeTypeHint;

        if (downloadUrl == null)
        {
            if (string.IsNullOrWhiteSpace(mediaId))
            {
                throw new InvalidOperationException("Meta no proporcionó un ID ni una URL válida para el archivo.");
            }

            var apiVersion = GetValue("WhatsApp:ApiVersion") ?? "v25.0";
            var phoneNumberId = ResolvePhoneNumberId(originPhoneNumberId);
            using var metadataRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(mediaId)}" +
                $"?phone_number_id={Uri.EscapeDataString(phoneNumberId)}");
            metadataRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var metadataResponse = await _httpClient.SendAsync(metadataRequest, cancellationToken);
            var metadataBody = await metadataResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!metadataResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Meta no devolvió la información del archivo. HTTP {(int)metadataResponse.StatusCode}: {metadataBody}");
            }

            using var metadata = JsonDocument.Parse(metadataBody);
            var root = metadata.RootElement;
            downloadUrl = root.GetProperty("url").GetString();
            metadataContentType = root.TryGetProperty("mime_type", out var mimeType)
                ? mimeType.GetString() ?? metadataContentType
                : metadataContentType;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("Meta no devolvió la URL del archivo.");
        }

        using var fileRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        fileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var fileResponse = await _httpClient.SendAsync(fileRequest, cancellationToken);
        if (!fileResponse.IsSuccessStatusCode)
        {
            var fileError = await fileResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Meta no permitió descargar el archivo. HTTP {(int)fileResponse.StatusCode}: {fileError}");
        }

        var bytes = await fileResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Meta devolvió un archivo vacío.");
        }
        var responseContentType = fileResponse.Content.Headers.ContentType?.MediaType;
        var contentType = !string.IsNullOrWhiteSpace(metadataContentType) &&
            !metadataContentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
                ? metadataContentType
                : !string.IsNullOrWhiteSpace(responseContentType)
                    ? responseContentType
                    : "application/octet-stream";

        return new WhatsAppMediaDownload(bytes, contentType);
    }

    private static string? TryGetMetaMediaUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("lookaside.fbsbx.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    public Task<string?> SendMediaMessageAsync(
        string to,
        string type,
        string url,
        string? fileName,
        string? caption,
        string? originPhoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = type.Equals("image", StringComparison.OrdinalIgnoreCase)
            ? "image"
            : "document";

        object media = normalizedType == "image"
            ? new { link = url, caption }
            : new { link = url, filename = fileName, caption };

        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = to,
            ["type"] = normalizedType,
            [normalizedType] = media
        };

        return SendMessageAsync(payload, originPhoneNumberId, cancellationToken);
    }

    public async Task<string> UploadMediaAsync(
        byte[] content,
        string contentType,
        string fileName,
        string? originPhoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        var accessToken = Required("WhatsApp:AccessToken");
        var phoneNumberId = ResolvePhoneNumberId(originPhoneNumberId);
        var apiVersion = GetValue("WhatsApp:ApiVersion") ?? "v25.0";
        var endpoint = $"https://graph.facebook.com/{apiVersion}/{phoneNumberId}/media";

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("whatsapp"), "messaging_product");
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);
        form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "archivo" : fileName);
        request.Content = form;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("META WHATSAPP MEDIA ERROR => HTTP {StatusCode}: {ResponseBody}",
                (int)response.StatusCode, responseBody);
            throw new InvalidOperationException(
                $"Meta rechazó el archivo. HTTP {(int)response.StatusCode}: {responseBody}");
        }

        using var json = JsonDocument.Parse(responseBody);
        if (!json.RootElement.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
        {
            throw new InvalidOperationException("Meta no devolvió el identificador del archivo.");
        }
        return id.GetString()!;
    }

    public Task<string?> SendMediaByIdAsync(
        string to,
        string type,
        string mediaId,
        string? fileName,
        string? caption,
        string? originPhoneNumberId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = type.Equals("image", StringComparison.OrdinalIgnoreCase)
            ? "image"
            : "document";
        object media = normalizedType == "image"
            ? new { id = mediaId, caption }
            : new { id = mediaId, filename = fileName, caption };
        var payload = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = to,
            ["type"] = normalizedType,
            [normalizedType] = media
        };
        return SendMessageAsync(payload, originPhoneNumberId, cancellationToken);
    }

    private async Task<string?> SendMessageAsync(
        object payload,
        string? originPhoneNumberId,
        CancellationToken cancellationToken)
    {
        var accessToken = Required("WhatsApp:AccessToken");
        var phoneNumberId = ResolvePhoneNumberId(originPhoneNumberId);
        var apiVersion = GetValue("WhatsApp:ApiVersion");
        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            apiVersion = "v25.0";
        }

        var endpoint = $"https://graph.facebook.com/{apiVersion}/{phoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "META WHATSAPP ERROR => HTTP {StatusCode}: {ResponseBody}",
                (int)response.StatusCode,
                responseBody);

            throw new InvalidOperationException(
                $"Meta rechazo el mensaje. HTTP {(int)response.StatusCode}: {responseBody}");
        }

        using var json = JsonDocument.Parse(responseBody);
        if (json.RootElement.TryGetProperty("messages", out var messages))
        {
            var firstMessage = messages.EnumerateArray().FirstOrDefault();
            if (firstMessage.ValueKind != JsonValueKind.Undefined &&
                firstMessage.TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }

        return null;
    }

    private string Required(string key)
    {
        var value = GetValue(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Falta configurar {key}.");
        }

        return value;
    }

    private string ResolvePhoneNumberId(string? requested)
    {
        var id = requested ?? _numbers.DefaultPhoneNumberId;
        if (!_numbers.Contains(id))
            throw new InvalidOperationException("El numero de WhatsApp no esta configurado para este WABA.");
        return id!;
    }

    private string? GetValue(string key) =>
        _socialIntegrations.GetConfiguredValue(key) ?? _configuration[key];
}

public sealed record WhatsAppMediaDownload(byte[] Content, string ContentType);
