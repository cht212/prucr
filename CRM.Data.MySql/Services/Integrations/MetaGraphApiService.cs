using System.Net.Http.Headers;
using System.Text.Json;
using CRM.Data.Models;
using CRM.Data.Services;

namespace CRM.Data.Services;

public sealed class MetaGraphApiService
{
    private readonly HttpClient _httpClient;
    private readonly SocialIntegrationService _socialIntegrations;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MetaGraphApiService> _logger;

    public MetaGraphApiService(
        HttpClient httpClient,
        SocialIntegrationService socialIntegrations,
        IConfiguration configuration,
        ILogger<MetaGraphApiService> logger)
    {
        _httpClient = httpClient;
        _socialIntegrations = socialIntegrations;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<MetaDashboardResult> ObtenerDashboardAsync(DateTime? desde, DateTime? hasta, CancellationToken cancellationToken = default)
    {
        var since = new DateTimeOffset((desde ?? DateTime.Today.AddDays(-30)).Date).ToUnixTimeSeconds();
        var until = new DateTimeOffset((hasta ?? DateTime.Today).Date.AddDays(1)).ToUnixTimeSeconds();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        var canales = await Task.WhenAll(
            ObtenerFacebookAsync(since, until, budget.Token),
            ObtenerInstagramAsync(since, until, budget.Token));

        return new MetaDashboardResult(
            canales,
            canales.All(canal => canal.Errors.Count == 0));
    }

    public async Task<MetaInsightsDiagnosticResult> DiagnosticarInsightsAsync(DateTime? desde, DateTime? hasta)
    {
        var since = new DateTimeOffset((desde ?? DateTime.Today.AddDays(-30)).Date).ToUnixTimeSeconds();
        var until = new DateTimeOffset((hasta ?? DateTime.Today).Date.AddDays(1)).ToUnixTimeSeconds();
        var pruebas = new List<MetaMetricDiagnostic>();

        pruebas.AddRange(await DiagnosticarFacebookInsightsAsync(since, until));
        pruebas.AddRange(await DiagnosticarInstagramInsightsAsync(since, until));

        return new MetaInsightsDiagnosticResult(
            desde?.Date ?? DateTime.Today.AddDays(-30),
            hasta?.Date ?? DateTime.Today,
            DateTimeOffset.UtcNow,
            pruebas);
    }

    public Task<MetaFacebookFeedResult> ObtenerFacebookFeedAsync(int limit = 10) =>
        ObtenerFacebookFeedAsync(null, null, limit);

    public async Task<MetaFacebookFeedResult> ObtenerFacebookFeedAsync(DateTime? desde, DateTime? hasta, int limit = 10, CancellationToken cancellationToken = default)
    {
        var pageId = GetValue("Meta:Facebook:PageId");
        var configuredToken = GetValue("Meta:Facebook:AccessToken");
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(configuredToken))
        {
            return MetaFacebookFeedResult.Failed(
                "Falta Meta:Facebook:PageId o Meta:Facebook:AccessToken.",
                ["Configura el Page ID y el Page Access Token en Conexiones > Facebook."]);
        }

        var apiVersion = GetValue("Meta:ApiVersion") ?? _configuration["Meta:ApiVersion"] ?? "v25.0";
        var pageToken = await ResolvePageAccessTokenAsync(apiVersion, pageId, configuredToken, cancellationToken);
        var since = new DateTimeOffset((desde ?? DateTime.Today.AddDays(-30)).Date).ToUnixTimeSeconds();
        var until = new DateTimeOffset((hasta ?? DateTime.Today).Date.AddDays(1)).ToUnixTimeSeconds();
        var fields = string.Join(",",
        [
            "id", "message", "created_time", "permalink_url", "full_picture", "shares",
            "attachments.limit(5){media_type,media,target{url},subattachments{media_type,media}}",
            "comments.limit(0).summary(total_count)",
            "reactions.limit(0).summary(total_count)",
            "reactions.type(LIKE).limit(0).summary(total_count).as(reaction_like)",
            "reactions.type(LOVE).limit(0).summary(total_count).as(reaction_love)",
            "reactions.type(HAHA).limit(0).summary(total_count).as(reaction_haha)",
            "reactions.type(WOW).limit(0).summary(total_count).as(reaction_wow)",
            "reactions.type(SAD).limit(0).summary(total_count).as(reaction_sorry)",
            "reactions.type(ANGRY).limit(0).summary(total_count).as(reaction_anger)"
        ]);
        var url = $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(pageId)}/published_posts?fields={Uri.EscapeDataString(fields)}&limit={Math.Clamp(limit, 1, 25)}&since={since}&until={until}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pageToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = BuildInsightError(body);
                var requirements = GetFeedRequirements((int)response.StatusCode, detail);
                return MetaFacebookFeedResult.Failed(
                    $"Meta Graph API HTTP {(int)response.StatusCode}: {detail}",
                    requirements);
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return new MetaFacebookFeedResult(true, [], null, []);
            }

            using var metricConcurrency = new SemaphoreSlim(5);
            var posts = await Task.WhenAll(data.EnumerateArray()
                .Select(post => EnrichFacebookPostAsync(post.Clone(), pageToken, apiVersion, metricConcurrency, cancellationToken)));
            return new MetaFacebookFeedResult(true, posts, null, []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo leer el feed de Facebook.");
            return MetaFacebookFeedResult.Failed(
                $"No se pudo consultar Facebook: {ex.Message}",
                ["Comprueba que la aplicación esté ejecutándose y que el token de Facebook siga vigente."]);
        }
    }

    private static IReadOnlyList<string> GetFeedRequirements(int statusCode, string detail)
    {
        if (detail.Contains("pages_read_engagement", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("Page Public Content Access", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                "El Page Token debe incluir pages_read_engagement.",
                "En Meta Developers solicita Page Public Content Access para la aplicación.",
                "Mientras Meta no apruebe esa función, el CRM puede mostrar Insights agregados, pero no el feed completo.",
                "Después de cambiar permisos, genera un token nuevo y guárdalo en Conexiones > Facebook."
            ];
        }

        if (statusCode == 401 || detail.Contains("Invalid OAuth", StringComparison.OrdinalIgnoreCase))
        {
            return ["Genera un Page Access Token nuevo para la página configurada y guárdalo en Conexiones > Facebook."];
        }

        return ["Revisa el Page ID, el Page Access Token y los permisos aprobados en Meta Developers."];
    }

    private static MetaFacebookPost ParseFacebookPost(JsonElement post)
    {
        var likes = HasSummary(post, "reactions")
            ? ReadSummaryCount(post, "reactions")
            : ReadSummaryCount(post, "likes");
        var comments = ReadSummaryCount(post, "comments");
        var shares = post.TryGetProperty("shares", out var sharesNode) &&
                     sharesNode.TryGetProperty("count", out var sharesCount) &&
                     sharesCount.TryGetInt32(out var shareTotal)
            ? shareTotal
            : 0;
        var reach = ReadInsightValue(post, "post_reach");
        var impressions = ReadInsightValue(post, "post_impressions");
        var engagement = ReadInsightValue(post, "post_engagement");
        var mediaUrl = ReadMediaUrl(post);
        var mediaType = ReadMediaType(post);

        var reactionsByType = ReadCurrentReactionBreakdown(post);

        return new MetaFacebookPost(
            ReadString(post, "id") ?? string.Empty,
            ReadString(post, "message") ?? "Publicación sin texto",
            ReadString(post, "created_time"),
            ReadString(post, "permalink_url"),
            likes,
            comments,
            shares,
            reach,
            impressions,
            engagement > 0 ? engagement : likes + comments + shares,
            mediaUrl,
            mediaType,
            reactionsByType);
    }

    private async Task<MetaFacebookPost> EnrichFacebookPostAsync(
        JsonElement post,
        string accessToken,
        string apiVersion,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken = default)
    {
        var parsed = ParseFacebookPost(post);
        if (string.IsNullOrWhiteSpace(parsed.Id)) return parsed;
        var hasCurrentReactionSummary = HasSummary(post, "reactions");
        var hasCurrentCommentSummary = HasSummary(post, "comments");

        await concurrency.WaitAsync(cancellationToken);
        try
        {
            var metrics =
                "post_media_view,post_activity_by_action_type,post_video_views,post_reactions_by_type_total";
            var url =
                $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(parsed.Id)}/insights" +
                $"?metric={Uri.EscapeDataString(metrics)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return parsed;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return parsed;
            }

            long reactions = parsed.Likes;
            long comments = parsed.Comments;
            long shares = parsed.Shares;
            long views = 0;
            var reactionsByType = parsed.ReactionsByType.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase);
            foreach (var metric in data.EnumerateArray())
            {
                var name = ReadString(metric, "name");
                if (!metric.TryGetProperty("values", out var values) ||
                    values.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var valueNode in values.EnumerateArray())
                {
                    if (!valueNode.TryGetProperty("value", out var value)) continue;
                    if (string.Equals(name, "post_reactions_by_type_total", StringComparison.OrdinalIgnoreCase) &&
                        !hasCurrentReactionSummary)
                    {
                        reactions = Math.Max(reactions, ReadLong(value));
                        if (value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var reaction in value.EnumerateObject())
                            {
                                reactionsByType[reaction.Name] = Math.Max(
                                    reactionsByType.GetValueOrDefault(reaction.Name),
                                    ReadLong(reaction.Value));
                            }
                        }
                    }
                    else if (string.Equals(name, "post_activity_by_action_type", StringComparison.OrdinalIgnoreCase) &&
                             value.ValueKind == JsonValueKind.Object)
                    {
                        if (!hasCurrentCommentSummary)
                            comments = Math.Max(comments, ReadObjectLong(value, "comment"));
                        shares = Math.Max(shares, ReadObjectLong(value, "share"));
                    }
                    else if (string.Equals(name, "post_media_view", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(name, "post_video_views", StringComparison.OrdinalIgnoreCase))
                    {
                        views = Math.Max(views, ReadLong(value));
                    }
                }
            }

            return parsed with
            {
                Likes = (int)Math.Min(int.MaxValue, reactions),
                Comments = (int)Math.Min(int.MaxValue, comments),
                Shares = (int)Math.Min(int.MaxValue, shares),
                Impressions = (int)Math.Min(int.MaxValue, views),
                Engagement = (int)Math.Min(int.MaxValue, reactions + comments + shares),
                ReactionsByType = reactionsByType
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "La consulta de Insights de Facebook venció para la publicación {PostId}.", parsed.Id);
            return parsed;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "No se pudieron consultar las métricas de Insights para la publicación {PostId}.", parsed.Id);
            return parsed;
        }
        finally
        {
            concurrency.Release();
        }
    }

    private static long ReadObjectLong(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(property, out var propertyValue)
            ? ReadLong(propertyValue)
            : 0;

    private static bool HasSummary(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) &&
        node.ValueKind == JsonValueKind.Object &&
        node.TryGetProperty("summary", out var summary) &&
        summary.ValueKind == JsonValueKind.Object &&
        summary.TryGetProperty("total_count", out _);

    private static IReadOnlyDictionary<string, long> ReadCurrentReactionBreakdown(JsonElement post)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reaction_like"] = "like",
            ["reaction_love"] = "love",
            ["reaction_haha"] = "haha",
            ["reaction_wow"] = "wow",
            ["reaction_sorry"] = "sorry",
            ["reaction_anger"] = "anger"
        };

        foreach (var alias in aliases)
        {
            if (!HasSummary(post, alias.Key)) continue;
            result[alias.Value] = ReadSummaryCount(post, alias.Key);
        }

        return result;
    }

    private static string? ReadMediaUrl(JsonElement root)
    {
        if (root.TryGetProperty("full_picture", out var fullPicture) && fullPicture.ValueKind == JsonValueKind.String)
        {
            return fullPicture.GetString();
        }

        if (root.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Object)
        {
            if (attachments.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
                    {
                        if (media.TryGetProperty("image", out var image) && image.TryGetProperty("src", out var src) && src.ValueKind == JsonValueKind.String)
                        {
                            return src.GetString();
                        }

                        if (media.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String)
                        {
                            return source.GetString();
                        }
                    }

                    if (item.TryGetProperty("media_type", out var mediaType) && mediaType.ValueKind == JsonValueKind.String)
                    {
                        if (item.TryGetProperty("media", out var mediaObj) && mediaObj.ValueKind == JsonValueKind.Object)
                        {
                            if (mediaObj.TryGetProperty("image", out var imageObj) && imageObj.TryGetProperty("src", out var src) && src.ValueKind == JsonValueKind.String)
                            {
                                return src.GetString();
                            }

                            if (mediaObj.TryGetProperty("source", out var sourceVal) && sourceVal.ValueKind == JsonValueKind.String)
                            {
                                return sourceVal.GetString();
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    private static string? ReadMediaType(JsonElement root)
    {
        if (root.TryGetProperty("is_video", out var isVideo) && isVideo.ValueKind == JsonValueKind.True)
        {
            return "video";
        }

        if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
        {
            var value = type.GetString();
            if (value?.Contains("video", StringComparison.OrdinalIgnoreCase) == true) return "video";
            if (value?.Contains("photo", StringComparison.OrdinalIgnoreCase) == true) return "photo";
        }

        if (root.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Object)
        {
            if (attachments.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("media_type", out var mediaType) && mediaType.ValueKind == JsonValueKind.String)
                    {
                        return mediaType.GetString();
                    }
                }
            }
        }

        return "photo";
    }

    private static int ReadSummaryCount(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var node) &&
            node.TryGetProperty("summary", out var summary) &&
            summary.TryGetProperty("total_count", out var count) &&
            count.TryGetInt32(out var value))
        {
            return value;
        }

        return 0;
    }

    private static int ReadInsightValue(JsonElement root, string metricName)
    {
        if (!root.TryGetProperty("insights", out var insights) || insights.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        if (!insights.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        foreach (var item in data.EnumerateArray())
        {
            var name = ReadString(item, "name");
            if (string.Equals(name, metricName, StringComparison.OrdinalIgnoreCase))
            {
                if (item.TryGetProperty("total_value", out var totalValue) && totalValue.ValueKind == JsonValueKind.Object)
                {
                    return ReadNumericValue(totalValue, "value");
                }

                if (item.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
                {
                    foreach (var valueItem in values.EnumerateArray())
                    {
                        var numeric = ReadNumericValue(valueItem, "value");
                        if (numeric > 0)
                        {
                            return numeric;
                        }
                    }
                }

                return ReadNumericValue(item, "value");
            }
        }

        return 0;
    }

    private static int ReadNumericValue(JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out var valueNode))
        {
            if (valueNode.ValueKind == JsonValueKind.Number && valueNode.TryGetInt32(out var intValue))
            {
                return intValue;
            }

            if (valueNode.ValueKind == JsonValueKind.String && int.TryParse(valueNode.GetString(), out var parsedValue))
            {
                return parsedValue;
            }
        }

        return 0;
    }

    public async Task<MetaCredentialsDiagnosticResult> DiagnosticarCredencialesAsync()
    {
        var apiVersion = GetValue("Meta:ApiVersion") ?? _configuration["Meta:ApiVersion"] ?? "v25.0";
        var appId = GetValue("Meta:AppId");
        var appSecret = GetValue("Meta:AppSecret");
        var checks = new List<MetaCredentialCheck>
        {
            await DiagnosticarInstagramLoginAsync(apiVersion),
            await DiagnosticarGraphCredentialAsync(
                "FACEBOOK_PAGE_TOKEN", "Page Token de Facebook",
                GetValue("Meta:Facebook:PageId"), GetValue("Meta:Facebook:AccessToken"),
                apiVersion, appId, appSecret, "page"),
            await DiagnosticarGraphCredentialAsync(
                "WHATSAPP_SYSTEM_USER_TOKEN", "Token de sistema de WhatsApp",
                GetValue("WhatsApp:PhoneNumberId"), GetValue("WhatsApp:AccessToken"),
                apiVersion, appId, appSecret, "phone")
        };

        return new MetaCredentialsDiagnosticResult(DateTimeOffset.UtcNow, apiVersion, checks);
    }

    private async Task<MetaCredentialCheck> DiagnosticarInstagramLoginAsync(string apiVersion)
    {
        const string key = "INSTAGRAM_LOGIN_TOKEN";
        const string name = "Instagram Login Access Token";
        var token = GetValue("Meta:Instagram:LoginAccessToken");
        var configuredId = GetValue("Meta:Instagram:InstagramBusinessAccountId") ??
            GetValue("Meta:Instagram:LoginUserId");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(configuredId))
        {
            return MetaCredentialCheck.Fail(key, name, "NO_CONFIGURADO",
                "Falta Instagram Professional User ID o Instagram Login Access Token.");
        }

        var url = $"https://graph.instagram.com/{apiVersion}/me?fields=id,user_id,username,account_type";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return MetaCredentialCheck.Fail(key, name,
                    ClassifyGraphError((int)response.StatusCode, body),
                    $"Instagram Graph API HTTP {(int)response.StatusCode}: {BuildInsightError(body)}");
            }

            using var document = JsonDocument.Parse(body);
            var returnedId = ReadString(document.RootElement, "user_id") ??
                ReadString(document.RootElement, "id");
            if (!string.Equals(returnedId, configuredId, StringComparison.OrdinalIgnoreCase))
            {
                return MetaCredentialCheck.Fail(key, name, "ID_NO_COINCIDE",
                    "El token pertenece a una cuenta de Instagram distinta del ID configurado.");
            }

            var conversationsUrl =
                $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(returnedId!)}/conversations" +
                "?fields=id&limit=1";
            using var conversationsRequest = new HttpRequestMessage(HttpMethod.Get, conversationsUrl);
            conversationsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var conversationsResponse = await _httpClient.SendAsync(conversationsRequest);
            if (!conversationsResponse.IsSuccessStatusCode)
            {
                var conversationsBody = await conversationsResponse.Content.ReadAsStringAsync();
                return MetaCredentialCheck.Fail(key, name, "SIN_PERMISO_MENSAJES",
                    $"Instagram no autorizo la lectura de mensajes: {BuildInsightError(conversationsBody)}");
            }

            var mediaUrl =
                $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(returnedId!)}/media" +
                "?fields=id&limit=1";
            using var mediaRequest = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
            mediaRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var mediaResponse = await _httpClient.SendAsync(mediaRequest);
            if (!mediaResponse.IsSuccessStatusCode)
            {
                var mediaBody = await mediaResponse.Content.ReadAsStringAsync();
                return MetaCredentialCheck.Fail(key, name, "SIN_PERMISO_PUBLICACIONES",
                    $"Instagram no autorizo la lectura de publicaciones: {BuildInsightError(mediaBody)}");
            }

            var accountType = ReadString(document.RootElement, "account_type") ?? "INSTAGRAM";
            return MetaCredentialCheck.Valid(key, name, "VALIDO",
                "Instagram confirmó el token y la cuenta profesional configurada.",
                accountType, null,
                ["instagram_business_basic", "instagram_business_manage_messages"]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo diagnosticar Instagram Login.");
            return MetaCredentialCheck.Fail(key, name, "ERROR_CONEXION", ex.Message);
        }
    }

    private async Task<MetaCredentialCheck> DiagnosticarGraphCredentialAsync(
        string key, string name, string? objectId, string? token, string apiVersion,
        string? appId, string? appSecret, string objectType)
    {
        if (string.IsNullOrWhiteSpace(objectId) || string.IsNullOrWhiteSpace(token))
        {
            return MetaCredentialCheck.Fail(key, name, "NO_CONFIGURADO", "Falta el identificador o el token.");
        }

        var fields = objectType == "phone" ? "id,display_phone_number,verified_name" : "id,name";
        var url = $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(objectId)}?fields={fields}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return MetaCredentialCheck.Fail(key, name,
                    ClassifyGraphError((int)response.StatusCode, body),
                    BuildGraphCredentialMessage((int)response.StatusCode, body));
            }

            using var document = JsonDocument.Parse(body);
            var returnedId = ReadString(document.RootElement, "id");
            if (!string.Equals(returnedId, objectId, StringComparison.OrdinalIgnoreCase))
            {
                return MetaCredentialCheck.Fail(key, name, "ID_NO_COINCIDE",
                    $"Meta respondió con el ID {returnedId ?? "vacío"}, pero está configurado {objectId}.");
            }

            var debug = await DebugTokenAsync(apiVersion, token, appId, appSecret);
            return debug.Success
                ? MetaCredentialCheck.Valid(key, name, "VALIDO",
                    objectType == "phone"
                        ? "El token puede consultar el Phone Number ID configurado."
                        : "El token puede consultar la Page ID configurada.",
                    debug.TokenType, debug.ExpiresAt, debug.Scopes)
                : MetaCredentialCheck.Fail(key, name, debug.Status, debug.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo diagnosticar la credencial {CredentialKey}.", key);
            return MetaCredentialCheck.Fail(key, name, "ERROR_CONEXION", ex.Message);
        }
    }

    private async Task<DebugTokenResult> DebugTokenAsync(
        string apiVersion, string token, string? appId, string? appSecret)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
        {
            return DebugTokenResult.Fail("APP_CREDENTIALS_FALTANTES",
                "Faltan Meta:AppId o Meta:AppSecret para depurar el token.");
        }

        var appToken = Uri.EscapeDataString($"{appId}|{appSecret}");
        var url = $"https://graph.facebook.com/{apiVersion}/debug_token?input_token={Uri.EscapeDataString(token)}&access_token={appToken}";
        using var response = await _httpClient.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            return DebugTokenResult.Fail(ClassifyGraphError((int)response.StatusCode, body),
                BuildGraphCredentialMessage((int)response.StatusCode, body));
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("is_valid", out var valid) || valid.ValueKind != JsonValueKind.True)
        {
            return DebugTokenResult.Fail("TOKEN_INVALIDO", "Meta indicó que el token no es válido.");
        }

        var expiresAt = data.TryGetProperty("expires_at", out var expiresNode) &&
                        expiresNode.TryGetInt64(out var expiresUnix) && expiresUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(expiresUnix)
            : (DateTimeOffset?)null;
        var scopes = data.TryGetProperty("scopes", out var scopesNode) &&
                     scopesNode.ValueKind == JsonValueKind.Array
            ? scopesNode.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!).ToArray()
            : [];
        var tokenType = data.TryGetProperty("type", out var typeNode) && typeNode.ValueKind == JsonValueKind.String
            ? typeNode.GetString() : null;

        return DebugTokenResult.Ok(tokenType, expiresAt, scopes);
    }

    private static string ClassifyGraphError(int statusCode, string body)
    {
        var message = BuildGraphCredentialMessage(statusCode, body);
        if (statusCode == 401 || message.Contains("invalid oauth", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("expired", StringComparison.OrdinalIgnoreCase)) return "TOKEN_INVALIDO";
        if (statusCode == 403 || message.Contains("permission", StringComparison.OrdinalIgnoreCase)) return "SIN_PERMISOS";
        return statusCode == 404 ? "ID_NO_ENCONTRADO" : "ERROR_META";
    }

    private static string BuildGraphCredentialMessage(int statusCode, string body) =>
        $"Graph API HTTP {statusCode}: {BuildInsightError(body)}";

    public async Task<InstagramLoginSyncResult> SincronizarInstagramLoginAsync(SocialInboundService inbound)
    {
        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v26.0";
        var token = GetValue("Meta:Instagram:LoginAccessToken") ??
            GetValue("Meta:Instagram:AccessToken");
        var ownUserId = GetValue("Meta:Instagram:LoginUserId");
        var professionalUserId = GetValue("Meta:Instagram:InstagramBusinessAccountId");

        if (string.IsNullOrWhiteSpace(token))
        {
            return new InstagramLoginSyncResult(false, 0, 0, 0, "Falta Meta:Instagram:LoginAccessToken.", null);
        }

        try
        {
            var me = await ObtenerInstagramLoginMeAsync(apiVersion, token);
            ownUserId ??= me.Id;
            professionalUserId = !string.IsNullOrWhiteSpace(me.UserId)
                ? me.UserId
                : professionalUserId;
            var conversationsOwnerId = !string.IsNullOrWhiteSpace(professionalUserId)
                ? professionalUserId
                : "me";

            var conversationsUrl =
                $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(conversationsOwnerId)}/conversations" +
                "?fields=id,participants,updated_time,message_count";

            using var conversationsRequest = new HttpRequestMessage(HttpMethod.Get, conversationsUrl);
            conversationsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var conversationsResponse = await _httpClient.SendAsync(conversationsRequest);
            var conversationsBody = await conversationsResponse.Content.ReadAsStringAsync();
            if (!conversationsResponse.IsSuccessStatusCode)
            {
                return new InstagramLoginSyncResult(
                    false,
                    0,
                    0,
                    0,
                    $"Instagram Login HTTP {(int)conversationsResponse.StatusCode}: {conversationsBody}",
                    BuildInstagramDiagnostic(conversationsOwnerId, conversationsUrl, conversationsBody));
            }

            using var conversationsDocument = JsonDocument.Parse(conversationsBody);
            if (!conversationsDocument.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return new InstagramLoginSyncResult(
                    true,
                    0,
                    0,
                    0,
                    null,
                    BuildInstagramDiagnostic(conversationsOwnerId, conversationsUrl, conversationsBody));
            }

            var conversaciones = 0;
            var mensajesLeidos = 0;
            var mensajesImportados = 0;

            foreach (var conversation in data.EnumerateArray())
            {
                var conversationId = ReadString(conversation, "id");
                if (string.IsNullOrWhiteSpace(conversationId)) continue;

                string? participantId = null;
                string? participantName = null;
                if (conversation.TryGetProperty("participants", out var participants) &&
                    participants.TryGetProperty("data", out var participantData) &&
                    participantData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var participant in participantData.EnumerateArray())
                    {
                        var candidateId = ReadString(participant, "id");
                        if (string.IsNullOrWhiteSpace(candidateId) ||
                            candidateId.Equals(ownUserId, StringComparison.OrdinalIgnoreCase) ||
                            candidateId.Equals(professionalUserId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        participantId = candidateId;
                        participantName = ReadString(participant, "username") ??
                            ReadString(participant, "name");
                        break;
                    }
                }

                conversaciones++;
                var imported = await SincronizarInstagramLoginConversationAsync(
                    apiVersion,
                    token,
                    ownUserId,
                    conversationId,
                    participantId,
                    participantName,
                    inbound);

                mensajesLeidos += imported.Leidos;
                mensajesImportados += imported.Importados;
            }

            return new InstagramLoginSyncResult(
                true,
                conversaciones,
                mensajesLeidos,
                mensajesImportados,
                null,
                BuildInstagramDiagnostic(conversationsOwnerId, conversationsUrl, conversationsBody));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar con Instagram Login API.");
            return new InstagramLoginSyncResult(false, 0, 0, 0, $"No se pudo conectar con Instagram API: {ex.Message}", null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Tiempo agotado conectando con Instagram Login API.");
            return new InstagramLoginSyncResult(false, 0, 0, 0, "Tiempo agotado conectando con Instagram API. Revisa internet/firewall y vuelve a intentar.", null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Instagram Login API devolvio JSON invalido.");
            return new InstagramLoginSyncResult(false, 0, 0, 0, $"Instagram devolvio una respuesta invalida: {ex.Message}", null);
        }
    }

    public async Task<InstagramLoginSyncResult> SincronizarFacebookAsync(SocialInboundService inbound)
    {
        var apiVersion = GetValue("Meta:ApiVersion") ?? _configuration["Meta:ApiVersion"] ?? "v26.0";
        var pageId = GetValue("Meta:Facebook:PageId");
        var configuredToken = GetValue("Meta:Facebook:AccessToken");
        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(configuredToken))
        {
            return new InstagramLoginSyncResult(false, 0, 0, 0,
                "Faltan Meta:Facebook:PageId o Meta:Facebook:AccessToken.", null);
        }

        try
        {
            var token = await ResolvePageAccessTokenAsync(apiVersion, pageId, configuredToken);
            var conversationsUrl =
                $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(pageId)}/conversations" +
                "?platform=messenger&fields=id,participants,updated_time&limit=25";
            using var request = new HttpRequestMessage(HttpMethod.Get, conversationsUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return new InstagramLoginSyncResult(false, 0, 0, 0,
                    $"Facebook Conversations HTTP {(int)response.StatusCode}: {body}", null);
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return new InstagramLoginSyncResult(true, 0, 0, 0, null, null);
            }

            var conversaciones = 0;
            var mensajesLeidos = 0;
            var mensajesProcesados = 0;
            foreach (var conversation in data.EnumerateArray())
            {
                var conversationId = ReadString(conversation, "id");
                if (string.IsNullOrWhiteSpace(conversationId)) continue;
                conversaciones++;
                var result = await SincronizarFacebookConversationAsync(
                    apiVersion, token, pageId, conversationId, inbound);
                mensajesLeidos += result.Leidos;
                mensajesProcesados += result.Importados;
            }

            return new InstagramLoginSyncResult(
                true, conversaciones, mensajesLeidos, mensajesProcesados, null, null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "No se pudo recuperar la bandeja de Facebook.");
            return new InstagramLoginSyncResult(false, 0, 0, 0, ex.Message, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Tiempo agotado recuperando la bandeja de Facebook.");
            return new InstagramLoginSyncResult(false, 0, 0, 0, ex.Message, null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Facebook devolvió una respuesta inválida al sincronizar mensajes.");
            return new InstagramLoginSyncResult(false, 0, 0, 0, ex.Message, null);
        }
    }

    private async Task<(int Leidos, int Importados)> SincronizarFacebookConversationAsync(
        string apiVersion,
        string token,
        string pageId,
        string conversationId,
        SocialInboundService inbound)
    {
        var url =
            $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(conversationId)}/messages" +
            "?fields=id,created_time,from,to,message&limit=50";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Facebook no devolvió mensajes para {ConversationId}. HTTP {StatusCode}: {Body}",
                conversationId, (int)response.StatusCode, body);
            return (0, 0);
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return (0, 0);
        }

        var leidos = 0;
        var procesados = 0;
        foreach (var message in data.EnumerateArray().Reverse())
        {
            leidos++;
            var messageId = ReadString(message, "id");
            var text = ReadString(message, "message");
            var fromId = ReadNestedString(message, "from", "id");
            var fromName = ReadNestedString(message, "from", "name") ?? "Facebook";
            var createdAt = ReadString(message, "created_time");
            var esReciente = DateTimeOffset.TryParse(createdAt, out var created) &&
                created >= DateTimeOffset.UtcNow.AddHours(-24);

            if (string.IsNullOrWhiteSpace(messageId) ||
                string.IsNullOrWhiteSpace(text) ||
                string.IsNullOrWhiteSpace(fromId) ||
                fromId.Equals(pageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await inbound.RegistrarMensajeEntranteAsync(new SocialInboundMessage(
                CanalSocial.Facebook,
                fromId,
                fromId,
                fromName,
                text,
                "text",
                messageId,
                null,
                esReciente));
            procesados++;
        }

        return (leidos, procesados);
    }

    private async Task<InstagramLoginMe> ObtenerInstagramLoginMeAsync(string apiVersion, string token)
    {
        var url = $"https://graph.instagram.com/{apiVersion}/me?fields=id,user_id,username,account_type";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode) return new InstagramLoginMe(null, null);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new InstagramLoginMe(
            ReadString(document.RootElement, "id"),
            ReadString(document.RootElement, "user_id"));
    }

    private async Task<(int Leidos, int Importados)> SincronizarInstagramLoginConversationAsync(
        string apiVersion,
        string token,
        string? ownUserId,
        string conversationId,
        string? participantId,
        string? participantName,
        SocialInboundService inbound)
    {
        var url =
            $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(conversationId)}/messages" +
            "?fields=id,created_time,from,to,message";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Instagram Login no devolvio mensajes para conversacion {ConversationId}. HTTP {StatusCode}: {Body}",
                conversationId,
                (int)response.StatusCode,
                body);
            return (0, 0);
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return (0, 0);
        }

        var leidos = 0;
        var importados = 0;
        var perfiles = new Dictionary<string, MetaContactProfile?>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in data.EnumerateArray().Reverse())
        {
            leidos++;
            var messageId = ReadString(message, "id");
            var text = ReadString(message, "message");
            var fromId = ReadNestedString(message, "from", "id");
            var fromUsername = ReadNestedString(message, "from", "username") ??
                ReadNestedString(message, "from", "name") ??
                "Instagram";
            var createdAt = ReadString(message, "created_time");
            var esReciente = DateTimeOffset.TryParse(createdAt, out var created) &&
                created >= DateTimeOffset.UtcNow.AddHours(-24);

            if (string.IsNullOrWhiteSpace(messageId) ||
                string.IsNullOrWhiteSpace(text) ||
                string.IsNullOrWhiteSpace(fromId) ||
                (!string.IsNullOrWhiteSpace(ownUserId) && fromId.Equals(ownUserId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            MetaContactProfile? profile = null;
            if (fromUsername.Equals("Instagram", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(participantName) &&
                fromId.Equals(participantId, StringComparison.OrdinalIgnoreCase))
            {
                fromUsername = participantName;
            }

            if (fromUsername.Equals("Instagram", StringComparison.OrdinalIgnoreCase))
            {
                if (!perfiles.TryGetValue(fromId, out profile))
                {
                    var profileResult = await ObtenerPerfilContactoDetalladoAsync(
                        CanalSocial.Instagram,
                        fromId,
                        ownUserId);
                    profile = profileResult.Profile;
                    perfiles[fromId] = profile;
                }

                fromUsername = profile?.Username ?? profile?.DisplayName ?? fromUsername;
            }

            await inbound.RegistrarMensajeEntranteAsync(new SocialInboundMessage(
                CanalSocial.Instagram,
                fromId,
                fromId,
                fromUsername,
                text,
                "text",
                messageId,
                profile?.ProfilePictureUrl,
                esReciente));

            importados++;
        }

        return (leidos, importados);
    }

    public async Task<MetaContactProfile?> ObtenerPerfilContactoAsync(
        string canal,
        string externalUserId,
        string? pageId = null)
    {
        var result = await ObtenerPerfilContactoDetalladoAsync(canal, externalUserId, pageId);
        return result.Profile;
    }

    public async Task<MetaProfileLookupResult> ObtenerPerfilContactoDetalladoAsync(
        string canal,
        string externalUserId,
        string? pageId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = CanalSocial.Normalizar(canal);
        if (normalized == CanalSocial.Instagram)
        {
            return await ObtenerPerfilInstagramDetalladoAsync(externalUserId, pageId, cancellationToken);
        }

        var configuredPageId = GetValue("Meta:Facebook:PageId");
        var configuredToken = GetValue("Meta:Facebook:AccessToken");
        if (string.IsNullOrWhiteSpace(configuredToken) || string.IsNullOrWhiteSpace(externalUserId))
        {
            return MetaProfileLookupResult.Failed(
                "Falta Page Access Token o identificador externo del contacto.",
                configuredPageId,
                pageId);
        }

        if (!string.IsNullOrWhiteSpace(pageId) &&
            !string.IsNullOrWhiteSpace(configuredPageId) &&
            !pageId.Equals(configuredPageId, StringComparison.OrdinalIgnoreCase))
        {
            return MetaProfileLookupResult.Failed(
                $"El mensaje llego para la pagina {pageId}, pero en Conexiones esta guardada la pagina {configuredPageId}. Guarda el Page Access Token de esa misma pagina.",
                configuredPageId,
                pageId);
        }

        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v25.0";
        var accessToken = await ResolvePageAccessTokenAsync(apiVersion, configuredPageId, configuredToken, cancellationToken);
        return await ConsultarPerfilMetaAsync(
            normalized,
            externalUserId,
            pageId,
            configuredPageId,
            accessToken,
            "https://graph.facebook.com",
            apiVersion,
            "first_name,last_name,name,profile_pic", cancellationToken);
    }

    private async Task<MetaProfileLookupResult> ObtenerPerfilInstagramDetalladoAsync(
        string externalUserId,
        string? webhookAccountId,
        CancellationToken cancellationToken = default)
    {
        var loginUserId = GetValue("Meta:Instagram:LoginUserId");
        var businessAccountId = GetValue("Meta:Instagram:InstagramBusinessAccountId");
        var legacyPageId = GetValue("Meta:Instagram:PageId");
        var loginToken = GetValue("Meta:Instagram:LoginAccessToken");
        var legacyToken = GetValue("Meta:Instagram:AccessToken");
        var configuredAccountId = loginUserId ?? businessAccountId ?? legacyPageId;

        if (string.IsNullOrWhiteSpace(externalUserId) ||
            (string.IsNullOrWhiteSpace(loginToken) && string.IsNullOrWhiteSpace(legacyToken)))
        {
            return MetaProfileLookupResult.Failed(
                "Falta un Access Token de Instagram o el identificador externo del contacto.",
                configuredAccountId,
                webhookAccountId);
        }

        var configuredIds = new[] { loginUserId, businessAccountId, legacyPageId }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(webhookAccountId) &&
            configuredIds.Length > 0 &&
            !configuredIds.Contains(webhookAccountId, StringComparer.OrdinalIgnoreCase))
        {
            return MetaProfileLookupResult.Failed(
                $"El mensaje llegó para la cuenta {webhookAccountId}, pero ese identificador no coincide con las cuentas configuradas de Instagram.",
                configuredAccountId,
                webhookAccountId);
        }

        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v25.0";
        var attempts = new List<(string Flow, string Host, string Token)>();
        if (!string.IsNullOrWhiteSpace(loginToken))
        {
            attempts.Add(("Instagram Login", "https://graph.instagram.com", loginToken));
        }

        var errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(legacyToken))
        {
            try
            {
                var pageToken = await ResolvePageAccessTokenAsync(apiVersion, legacyPageId, legacyToken, cancellationToken);
                attempts.Add(("Facebook Login", "https://graph.facebook.com", pageToken));
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                errors.Add($"Facebook Login: {ex.Message}");
            }
        }

        foreach (var attempt in attempts)
        {
            var result = await ConsultarPerfilMetaAsync(
                CanalSocial.Instagram,
                externalUserId,
                webhookAccountId,
                configuredAccountId,
                attempt.Token,
                attempt.Host,
                apiVersion,
                "name,username,profile_pic", cancellationToken);
            if (result.Success)
            {
                return result;
            }

            errors.Add($"{attempt.Flow}: {result.Error}");
        }

        return MetaProfileLookupResult.Failed(
            string.Join(" | ", errors.Distinct(StringComparer.OrdinalIgnoreCase)),
            configuredAccountId,
            webhookAccountId);
    }

    private async Task<MetaProfileLookupResult> ConsultarPerfilMetaAsync(
        string canal,
        string externalUserId,
        string? webhookAccountId,
        string? configuredAccountId,
        string accessToken,
        string graphHost,
        string apiVersion,
        string fields,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{graphHost}/{apiVersion}/{Uri.EscapeDataString(externalUserId)}" +
            $"?fields={Uri.EscapeDataString(fields)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Meta Graph API no devolvio perfil para {Canal}/{ExternalUserId}. HTTP {StatusCode}: {Body}",
                    canal,
                    externalUserId,
                    (int)response.StatusCode,
                    body);
                return MetaProfileLookupResult.Failed(
                    $"HTTP {(int)response.StatusCode}: {BuildInsightError(body)}",
                    configuredAccountId,
                    webhookAccountId);
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var firstName = ReadString(root, "first_name");
            var lastName = ReadString(root, "last_name");
            var name = ReadString(root, "name");
            var username = ReadString(root, "username");
            var picture = ReadString(root, "profile_pic");
            var displayName = canal == CanalSocial.Instagram
                ? username ?? name
                : JoinName(firstName, lastName) ?? name ?? username;

            if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(picture))
            {
                return MetaProfileLookupResult.Failed(
                    "Meta respondio, pero no devolvio nombre ni foto para este usuario.",
                    configuredAccountId,
                    webhookAccountId);
            }

            return MetaProfileLookupResult.Ok(
                new MetaContactProfile(displayName, username, picture),
                configuredAccountId,
                webhookAccountId);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(
                ex,
                "No se pudo consultar perfil de Meta para {Canal}/{ExternalUserId}.",
                canal,
                externalUserId);
            return MetaProfileLookupResult.Failed(ex.Message, configuredAccountId, webhookAccountId);
        }
    }

    public async Task<MetaPublicationCommentsResult> ObtenerComentariosPublicacionAsync(
        string canal,
        string publicationId,
        CancellationToken cancellationToken = default)
    {
        var normalized = CanalSocial.Normalizar(canal);
        if (normalized is not (CanalSocial.Facebook or CanalSocial.Instagram) ||
            string.IsNullOrWhiteSpace(publicationId))
        {
            return MetaPublicationCommentsResult.Failed("Canal o publicación no válidos.");
        }

        var apiVersion = GetValue("Meta:ApiVersion") ?? _configuration["Meta:ApiVersion"] ?? "v26.0";
        if (normalized == CanalSocial.Instagram)
        {
            var candidates = new[]
            {
                new
                {
                    Host = "graph.instagram.com",
                    Token = GetValue("Meta:Instagram:LoginAccessToken"),
                    Permission = "instagram_business_manage_comments"
                },
                new
                {
                    Host = "graph.facebook.com",
                    Token = GetValue("Meta:Instagram:AccessToken"),
                    Permission = "instagram_manage_comments"
                }
            }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Token))
            .DistinctBy(candidate => $"{candidate.Host}:{candidate.Token}")
            .ToArray();

            if (candidates.Length == 0)
                return MetaPublicationCommentsResult.Failed("Falta el Access Token de Instagram.");

            MetaPublicationCommentsResult? emptyResult = null;
            var errors = new List<string>();
            foreach (var candidate in candidates)
            {
                var url = $"https://{candidate.Host}/{apiVersion}/{Uri.EscapeDataString(publicationId)}/comments" +
                    "?fields=id,text,timestamp,username,from{id,username}&limit=100";
                var result = await FetchPublicationCommentsAsync(
                    normalized,
                    publicationId,
                    url,
                    candidate.Token!,
                    cancellationToken);
                if (result.Success && result.Comments.Count > 0) return result;
                if (result.Success)
                {
                    emptyResult ??= result;
                    continue;
                }

                errors.Add($"{candidate.Host}: {result.Error}");
                _logger.LogWarning(
                    "No se pudieron leer comentarios de Instagram mediante {Host}. Permiso esperado: {Permission}. Error: {Error}",
                    candidate.Host,
                    candidate.Permission,
                    result.Error);
            }

            if (emptyResult != null && errors.Count == 0) return emptyResult;
            return MetaPublicationCommentsResult.Failed(
                "Instagram no autorizó la lectura de comentarios. Reconecta la cuenta y concede " +
                "instagram_business_manage_comments. " + string.Join(" | ", errors));
        }

        var ownerId = GetValue("Meta:Facebook:PageId") ?? string.Empty;
        var configuredToken = GetValue("Meta:Facebook:AccessToken") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(configuredToken))
            return MetaPublicationCommentsResult.Failed("Faltan Page ID o Access Token de Facebook.");
        var accessToken = await ResolvePageAccessTokenAsync(apiVersion, ownerId, configuredToken);
        var facebookUrl = $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(publicationId)}/comments" +
            "?fields=id,message,created_time,from{id,name}&limit=100";
        return await FetchPublicationCommentsAsync(
            normalized,
            publicationId,
            facebookUrl,
            accessToken,
            cancellationToken);
    }

    private async Task<MetaPublicationCommentsResult> FetchPublicationCommentsAsync(
        string normalized,
        string publicationId,
        string initialUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return MetaPublicationCommentsResult.Failed($"Falta el Access Token de {normalized}.");

        var result = new List<MetaPublicationComment>();
        var url = initialUrl;
        for (var page = 0; page < 3 && !string.IsNullOrWhiteSpace(url); page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Meta no devolvio comentarios para {Canal}/{PublicationId}. HTTP {StatusCode}: {Body}",
                    normalized,
                    publicationId,
                    (int)response.StatusCode,
                    body);
                var detail = BuildInsightError(body);
                if (normalized == CanalSocial.Facebook &&
                    detail.Contains("pages_read_user_content", StringComparison.OrdinalIgnoreCase))
                {
                    detail = "Facebook requiere el permiso pages_read_user_content para leer los comentarios. Reconecta Facebook desde Conexiones y autoriza el permiso.";
                }
                return MetaPublicationCommentsResult.Failed(detail);
            }

            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var item in data.EnumerateArray())
            {
                var id = ReadString(item, "id");
                var userId = ReadNestedString(item, "from", "id");
                var username = normalized == CanalSocial.Instagram
                    ? ReadString(item, "username") ?? ReadNestedString(item, "from", "username")
                    : ReadNestedString(item, "from", "name");
                var text = normalized == CanalSocial.Instagram
                    ? ReadString(item, "text")
                    : ReadString(item, "message");
                var createdRaw = normalized == CanalSocial.Instagram
                    ? ReadString(item, "timestamp")
                    : ReadString(item, "created_time");

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                userId ??= username;
                if (string.IsNullOrWhiteSpace(userId))
                {
                    // Meta puede ocultar `from` por privacidad aunque permita leer y
                    // responder el comentario. El ID del comentario mantiene estable
                    // el registro sin inventar la identidad de la persona.
                    userId = $"comment:{id}";
                    username = normalized == CanalSocial.Instagram
                        ? "Usuario de Instagram"
                        : "Usuario de Facebook";
                }
                DateTimeOffset? createdAt = DateTimeOffset.TryParse(createdRaw, out var parsed) ? parsed : null;
                result.Add(new MetaPublicationComment(
                    normalized,
                    publicationId,
                    id,
                    userId,
                    username,
                    text,
                    createdAt));
            }

            url = document.RootElement.TryGetProperty("paging", out var paging) &&
                paging.TryGetProperty("next", out var next) &&
                next.ValueKind == JsonValueKind.String
                    ? next.GetString() ?? string.Empty
                    : string.Empty;
        }

        return MetaPublicationCommentsResult.Ok(result);
    }

    private async Task<string> ResolvePageAccessTokenAsync(
        string apiVersion,
        string? pageId,
        string configuredToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return configuredToken;
        }

        var meUrl =
            $"https://graph.facebook.com/{apiVersion}/me" +
            $"?fields=id,name&access_token={Uri.EscapeDataString(configuredToken)}";

        try
        {
            using var meResponse = await _httpClient.GetAsync(meUrl, cancellationToken);
            var meBody = await meResponse.Content.ReadAsStringAsync(cancellationToken);
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
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(ex, "No se pudo validar si el token de Meta pertenece a la pagina.");
        }

        var accountsUrl =
            $"https://graph.facebook.com/{apiVersion}/me/accounts" +
            $"?fields=id,name,access_token&access_token={Uri.EscapeDataString(configuredToken)}";

        using var accountsResponse = await _httpClient.GetAsync(accountsUrl, cancellationToken);
        var accountsBody = await accountsResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!accountsResponse.IsSuccessStatusCode)
        {
            return configuredToken;
        }

        using var accountsDocument = JsonDocument.Parse(accountsBody);
        if (!accountsDocument.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return configuredToken;
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

        return configuredToken;
    }

    private async Task<MetaChannelInsight> ObtenerFacebookAsync(long since, long until, CancellationToken cancellationToken = default)
    {
        var pageId = GetValue("Meta:Facebook:PageId");
        var token = GetValue("Meta:Facebook:AccessToken");
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(pageId))
        {
            missing.Add("Meta:Facebook:PageId");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            missing.Add("Meta:Facebook:AccessToken con permiso read_insights");
        }

        if (missing.Count > 0)
        {
            return MetaChannelInsight.NotConfigured(
                CanalSocial.Facebook,
                "Facebook",
                "Faltan credenciales para leer Page Insights.",
                missing);
        }

        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v25.0";
        var insightsToken = await ResolvePageAccessTokenAsync(apiVersion, pageId!, token!, cancellationToken);

        var metrics = await GetInsightsAsync(
            pageId!,
            insightsToken,
            "page_media_view,page_total_media_view_unique,page_post_engagements,page_follows",
            "day",
            since,
            until, cancellationToken);

        var hasErrors = metrics.Errors.Count > 0;
        var impresiones = GetMetric(metrics, "page_media_view");
        var alcance = GetMetric(metrics, "page_total_media_view_unique");
        var interacciones = GetMetric(metrics, "page_post_engagements");
        var seguidores = GetMetric(metrics, "page_follows");

        return new MetaChannelInsight(
            CanalSocial.Facebook,
            "Facebook",
            true,
            hasErrors ? "ERROR" : (impresiones + interacciones + alcance + seguidores > 0 ? "OPERATIVO" : "SIN_DATOS"),
            hasErrors
                ? "Meta rechazo la consulta de Page Insights. Revisa token, pagina, permiso read_insights y metricas disponibles para tu pagina."
                : (impresiones + interacciones + alcance + seguidores > 0
                    ? "Estadisticas de Facebook leidas correctamente desde Graph API."
                    : "Meta respondio correctamente, pero no devolvio valores para el rango consultado."),
            GetInsightRequirements(metrics.Errors),
            alcance,
            impresiones,
            interacciones,
            0,
            0,
            seguidores,
            metrics.Errors,
            DateTimeOffset.UtcNow);
    }

    private async Task<MetaChannelInsight> ObtenerInstagramAsync(long since, long until, CancellationToken cancellationToken = default)
    {
        var instagramId = GetValue("Meta:Instagram:InstagramBusinessAccountId") ??
            GetValue("Meta:Instagram:LoginUserId");
        var token = GetValue("Meta:Instagram:LoginAccessToken") ??
            GetValue("Meta:Instagram:AccessToken");
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(instagramId))
        {
            missing.Add("Meta:Instagram:InstagramBusinessAccountId");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            missing.Add("Meta:Instagram:LoginAccessToken con permiso instagram_business_basic");
        }

        if (missing.Count > 0)
        {
            return MetaChannelInsight.NotConfigured(
                CanalSocial.Instagram,
                "Instagram",
                "Faltan credenciales de Instagram Login para leer las metricas.",
                missing);
        }

        var metrics = await GetInstagramInsightsAsync(
            instagramId!,
            token!,
            "views,reach,total_interactions,profile_views,website_clicks",
            since,
            until, cancellationToken);

        var hasErrors = metrics.Errors.Count > 0;
        var alcance = GetMetric(metrics, "reach");
        var visualizaciones = GetMetric(metrics, "views");
        var interacciones = GetMetric(metrics, "total_interactions");
        var visitasPerfil = GetMetric(metrics, "profile_views");
        var clicksSitio = GetMetric(metrics, "website_clicks");
        var audience = await GetInstagramAudienceBreakdownAsync(
            instagramId!, token!, since, until, cancellationToken);

        return new MetaChannelInsight(
            CanalSocial.Instagram,
            "Instagram",
            true,
            hasErrors ? "ERROR" : (visualizaciones + alcance + interacciones + visitasPerfil + clicksSitio > 0 ? "OPERATIVO" : "SIN_DATOS"),
            hasErrors
                ? $"Instagram Insights no respondió: {metrics.Errors[0]}"
                : (visualizaciones + alcance + interacciones + visitasPerfil + clicksSitio > 0
                    ? "Estadisticas de Instagram leidas correctamente desde Instagram Graph API."
                    : "Meta respondio correctamente, pero no devolvio valores para el rango consultado."),
            [],
            alcance,
            visualizaciones,
            interacciones,
            visitasPerfil,
            clicksSitio,
            0,
            metrics.Errors,
            DateTimeOffset.UtcNow,
            audience.FollowerViews,
            audience.NonFollowerViews,
            audience.Ages);
    }

    private async Task<MetaAudienceBreakdown> GetInstagramAudienceBreakdownAsync(
        string instagramId,
        string accessToken,
        long since,
        long until,
        CancellationToken cancellationToken = default)
    {
        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v26.0";
        var followerViews = 0L;
        var nonFollowerViews = 0L;
        var ages = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        async Task<JsonDocument?> ReadAsync(string query)
        {
            var url = $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(instagramId)}/insights?{query}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return null;
            }
        }

        using (var viewsDocument = await ReadAsync(
            $"metric=views&period=day&metric_type=total_value&breakdown=follow_type&since={since}&until={until}"))
        {
            if (viewsDocument != null)
            {
                foreach (var result in ReadBreakdownResults(viewsDocument.RootElement))
                {
                    var dimension = result.Dimension.ToUpperInvariant();
                    if (dimension == "FOLLOWER") followerViews += result.Value;
                    if (dimension == "NON_FOLLOWER") nonFollowerViews += result.Value;
                }
            }
        }

        using (var ageDocument = await ReadAsync(
            "metric=follower_demographics&period=lifetime&metric_type=total_value&breakdown=age&timeframe=last_30_days"))
        {
            if (ageDocument != null)
            {
                foreach (var result in ReadBreakdownResults(ageDocument.RootElement))
                {
                    ages[result.Dimension] = result.Value;
                }
            }
        }

        return new MetaAudienceBreakdown(followerViews, nonFollowerViews, ages);
    }

    private static IEnumerable<(string Dimension, long Value)> ReadBreakdownResults(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var metric in data.EnumerateArray())
        {
            if (!metric.TryGetProperty("total_value", out var totalValue) ||
                !totalValue.TryGetProperty("breakdowns", out var breakdowns) ||
                breakdowns.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var breakdown in breakdowns.EnumerateArray())
            {
                if (!breakdown.TryGetProperty("results", out var results) ||
                    results.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var result in results.EnumerateArray())
                {
                    if (!result.TryGetProperty("dimension_values", out var dimensions) ||
                        dimensions.ValueKind != JsonValueKind.Array ||
                        dimensions.GetArrayLength() == 0 ||
                        dimensions[0].ValueKind != JsonValueKind.String ||
                        !result.TryGetProperty("value", out var value))
                        continue;

                    yield return (dimensions[0].GetString() ?? string.Empty, ReadLong(value));
                }
            }
        }
    }

    private async Task<IReadOnlyList<MetaMetricDiagnostic>> DiagnosticarFacebookInsightsAsync(long since, long until)
    {
        var pageId = GetValue("Meta:Facebook:PageId");
        var token = GetValue("Meta:Facebook:AccessToken");
        var metrics = new[]
        {
            ("page_media_view", "Vistas de contenido"),
            ("page_total_media_view_unique", "Personas alcanzadas"),
            ("page_post_engagements", "Interacciones con publicaciones"),
            ("page_follows", "Seguidores")
        };

        if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(token))
        {
            return metrics.Select(metric => MetaMetricDiagnostic.NotConfigured(
                CanalSocial.Facebook,
                "Facebook",
                metric.Item1,
                metric.Item2,
                "Falta Meta:Facebook:PageId o Meta:Facebook:AccessToken.")).ToArray();
        }

        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v25.0";
        var insightsToken = await ResolvePageAccessTokenAsync(apiVersion, pageId, token);
        return await DiagnosticarMetricasAsync(CanalSocial.Facebook, "Facebook", pageId, insightsToken, metrics, since, until);
    }

    private async Task<IReadOnlyList<MetaMetricDiagnostic>> DiagnosticarInstagramInsightsAsync(long since, long until)
    {
        var instagramId = GetValue("Meta:Instagram:InstagramBusinessAccountId") ??
            GetValue("Meta:Instagram:LoginUserId");
        var token = GetValue("Meta:Instagram:LoginAccessToken") ??
            GetValue("Meta:Instagram:AccessToken");
        var metrics = new[]
        {
            ("views", "Vistas"),
            ("reach", "Alcance"),
            ("total_interactions", "Interacciones"),
            ("profile_views", "Visitas al perfil"),
            ("website_clicks", "Clicks al sitio web")
        };

        if (string.IsNullOrWhiteSpace(instagramId) || string.IsNullOrWhiteSpace(token))
        {
            return metrics.Select(metric => MetaMetricDiagnostic.NotConfigured(
                CanalSocial.Instagram,
                "Instagram",
                metric.Item1,
                metric.Item2,
                "Falta Instagram Professional User ID o Instagram Login Access Token.")).ToArray();
        }

        return await DiagnosticarMetricasInstagramAsync(instagramId, token, metrics, since, until);
    }

    private async Task<IReadOnlyList<MetaMetricDiagnostic>> DiagnosticarMetricasInstagramAsync(
        string objectId,
        string token,
        IReadOnlyList<(string Key, string Label)> metrics,
        long since,
        long until)
    {
        var result = new List<MetaMetricDiagnostic>();
        foreach (var metric in metrics)
        {
            var response = await GetInstagramInsightsAsync(objectId, token, metric.Key, since, until);
            var ok = response.Errors.Count == 0;
            var value = GetMetric(response, metric.Key);
            result.Add(new MetaMetricDiagnostic(
                CanalSocial.Instagram,
                "Instagram",
                metric.Key,
                metric.Label,
                true,
                ok,
                ok ? (value > 0 ? "CON_DATOS" : "CERO") : "ERROR",
                value,
                ok
                    ? (value > 0 ? "Instagram devolvio valores para esta metrica." : "Instagram acepto la metrica, pero devolvio 0 en el rango consultado.")
                    : response.Errors[0],
                response.Errors));
        }

        return result;
    }

    private async Task<IReadOnlyList<MetaMetricDiagnostic>> DiagnosticarMetricasAsync(
        string canal,
        string nombre,
        string objectId,
        string token,
        IReadOnlyList<(string Key, string Label)> metrics,
        long since,
        long until)
    {
        var result = new List<MetaMetricDiagnostic>();
        foreach (var metric in metrics)
        {
            var response = await GetInsightsAsync(objectId, token, metric.Key, "day", since, until);
            var ok = response.Errors.Count == 0;
            var value = GetMetric(response, metric.Key);
            result.Add(new MetaMetricDiagnostic(
                canal,
                nombre,
                metric.Key,
                metric.Label,
                true,
                ok,
                ok ? (value > 0 ? "CON_DATOS" : "CERO") : "ERROR",
                value,
                ok
                    ? (value > 0 ? "Meta devolvio valores para esta metrica." : "Meta acepto la metrica, pero devolvio 0 en el rango consultado.")
                    : response.Errors[0],
                response.Errors));
        }

        return result;
    }

    private async Task<MetaInsightMetrics> GetInsightsAsync(
        string objectId,
        string accessToken,
        string metric,
        string period,
        long since,
        long until,
        CancellationToken cancellationToken = default)
    {
        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v25.0";
        var url =
            $"https://graph.facebook.com/{apiVersion}/{Uri.EscapeDataString(objectId)}/insights" +
            $"?metric={Uri.EscapeDataString(metric)}" +
            $"&period={Uri.EscapeDataString(period)}" +
            $"&since={since}" +
            $"&until={until}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Meta Graph API devolvió HTTP {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    body);

                return MetaInsightMetrics.WithError($"HTTP {(int)response.StatusCode}: {BuildInsightError(body)}");
            }

            return ParseMetrics(body);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(ex, "No se pudieron obtener métricas de Meta para {ObjectId}.", objectId);
            return MetaInsightMetrics.WithError(ex.Message);
        }
    }

    private async Task<MetaInsightMetrics> GetInstagramInsightsAsync(
        string objectId,
        string accessToken,
        string metric,
        long since,
        long until,
        CancellationToken cancellationToken = default)
    {
        var apiVersion = GetValue("Meta:ApiVersion") ??
            _configuration["Meta:ApiVersion"] ??
            "v26.0";
        var url =
            $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(objectId)}/insights" +
            $"?metric={Uri.EscapeDataString(metric)}" +
            "&period=day" +
            $"&since={since}" +
            $"&until={until}" +
            "&metric_type=total_value";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Instagram Graph API devolvio HTTP {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    body);
                return MetaInsightMetrics.WithError($"HTTP {(int)response.StatusCode}: {BuildInsightError(body)}");
            }

            return ParseMetrics(body);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(ex, "No se pudieron obtener metricas de Instagram para {ObjectId}.", objectId);
            return MetaInsightMetrics.WithError(ex.Message);
        }
    }

    private static MetaInsightMetrics ParseMetrics(string body)
    {
        var metrics = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return new MetaInsightMetrics(metrics, []);
        }

        foreach (var metricNode in data.EnumerateArray())
        {
            if (!metricNode.TryGetProperty("name", out var nameProperty)) continue;
            var name = nameProperty.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;

            long total = 0;
            if (metricNode.TryGetProperty("total_value", out var totalValue) &&
                totalValue.ValueKind == JsonValueKind.Object &&
                totalValue.TryGetProperty("value", out var totalValueNode))
            {
                total = ReadLong(totalValueNode);
            }

            if (metricNode.TryGetProperty("values", out var values) &&
                values.ValueKind == JsonValueKind.Array)
            {
                foreach (var valueNode in values.EnumerateArray())
                {
                    if (!valueNode.TryGetProperty("value", out var value)) continue;
                    total += ReadLong(value);
                }
            }

            metrics[name] = total;
        }

        return new MetaInsightMetrics(metrics, []);
    }

    private static long ReadLong(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            long total = 0;
            foreach (var property in value.EnumerateObject())
            {
                total += ReadLong(property.Value);
            }

            return total;
        }

        return 0;
    }

    private static long GetMetric(MetaInsightMetrics metrics, string key) =>
        metrics.Values.TryGetValue(key, out var value) ? value : 0;

    private static string BuildInsightError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "Meta no devolvio detalle del error.";
            }
        }
        catch
        {
            // Si Meta no devuelve JSON valido, se muestra una version corta del cuerpo.
        }

        return body.Length > 220 ? $"{body[..220]}..." : body;
    }

    private static IReadOnlyList<string> GetInsightRequirements(IReadOnlyList<string> errors)
    {
        if (errors.Count == 0) return [];

        var requirements = new List<string>();
        foreach (var error in errors)
        {
            if (error.Contains("Page Access Token", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add("Usar Page Access Token de la misma pagina configurada.");
            }

            if (error.Contains("read_insights", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("permissions", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add("Conceder permiso read_insights al token.");
            }

            if (error.Contains("valid insights metric", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("metric", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Add("Revisar metricas disponibles para la pagina en la version actual de Meta.");
            }
        }

        return requirements.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadNestedString(JsonElement root, string parent, string property) =>
        root.TryGetProperty(parent, out var parentNode) &&
        parentNode.ValueKind == JsonValueKind.Object &&
        parentNode.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string BuildInstagramDiagnostic(string ownerId, string url, string body)
    {
        var safeUrl = url.Split('?')[0];
        var shortBody = body.Length > 500 ? $"{body[..500]}..." : body;
        return $"Owner usado: {ownerId}; endpoint: {safeUrl}; respuesta: {shortBody}";
    }

    private static string? JoinName(string? firstName, string? lastName)
    {
        var parts = new[] { firstName, lastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();

        return parts.Length == 0 ? null : string.Join(" ", parts);
    }

    private string? GetValue(string key) =>
        _socialIntegrations.GetConfiguredValue(key) ?? _configuration[key];
}

public sealed record MetaContactProfile(
    string? DisplayName,
    string? Username,
    string? ProfilePictureUrl);

public sealed record MetaPublicationComment(
    string Canal,
    string PublicationId,
    string Id,
    string UserId,
    string? Username,
    string Text,
    DateTimeOffset? CreatedAt);

public sealed record MetaPublicationCommentsResult(
    bool Success,
    IReadOnlyList<MetaPublicationComment> Comments,
    string? Error)
{
    public static MetaPublicationCommentsResult Ok(IReadOnlyList<MetaPublicationComment> comments) =>
        new(true, comments, null);

    public static MetaPublicationCommentsResult Failed(string error) =>
        new(false, [], error);
}

public sealed record InstagramLoginSyncResult(
    bool Success,
    int Conversaciones,
    int MensajesLeidos,
    int MensajesImportados,
    string? Error,
    string? Diagnostic);

internal sealed record InstagramLoginMe(
    string? Id,
    string? UserId);

public sealed record MetaProfileLookupResult(
    bool Success,
    MetaContactProfile? Profile,
    string? Error,
    string? ConfiguredPageId,
    string? WebhookPageId)
{
    public static MetaProfileLookupResult Ok(
        MetaContactProfile profile,
        string? configuredPageId,
        string? webhookPageId) =>
        new(true, profile, null, configuredPageId, webhookPageId);

    public static MetaProfileLookupResult Failed(
        string error,
        string? configuredPageId,
        string? webhookPageId) =>
        new(false, null, error, configuredPageId, webhookPageId);
}

public sealed record MetaDashboardResult(
    IReadOnlyList<MetaChannelInsight> Canales,
    bool Success);

public sealed record MetaInsightsDiagnosticResult(
    DateTime Desde,
    DateTime Hasta,
    DateTimeOffset RevisadoEn,
    IReadOnlyList<MetaMetricDiagnostic> Pruebas);

public sealed record MetaCredentialsDiagnosticResult(
    DateTimeOffset RevisadoEn,
    string ApiVersion,
    IReadOnlyList<MetaCredentialCheck> Pruebas);

public sealed record MetaFacebookFeedResult(
    bool Success,
    IReadOnlyList<MetaFacebookPost> Posts,
    string? Error,
    IReadOnlyList<string> Requirements)
{
    public static MetaFacebookFeedResult Failed(string error, IReadOnlyList<string> requirements) =>
        new(false, [], error, requirements);
}

public sealed record MetaFacebookPost(
    string Id,
    string Message,
    string? CreatedTime,
    string? PermalinkUrl,
    int Likes,
    int Comments,
    int Shares,
    int Reach,
    int Impressions,
    int Engagement,
    string? MediaUrl,
    string? MediaType,
    IReadOnlyDictionary<string, long> ReactionsByType);

public sealed record MetaCredentialCheck(
    string Clave,
    string Nombre,
    string Estado,
    bool Ok,
    string Mensaje,
    string? TipoToken,
    DateTimeOffset? ExpiraEn,
    IReadOnlyList<string> Permisos)
{
    public static MetaCredentialCheck Fail(string key, string name, string status, string message) =>
        new(key, name, status, false, message, null, null, []);

    public static MetaCredentialCheck Valid(
        string key,
        string name,
        string status,
        string message,
        string? tokenType,
        DateTimeOffset? expiresAt,
        IReadOnlyList<string> scopes) =>
        new(key, name, status, true, message, tokenType, expiresAt, scopes);
}

internal sealed record DebugTokenResult(
    bool Success,
    string Status,
    string Message,
    string? TokenType,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes)
{
    public static DebugTokenResult Fail(string status, string message) =>
        new(false, status, message, null, null, []);

    public static DebugTokenResult Ok(
        string? tokenType,
        DateTimeOffset? expiresAt,
        IReadOnlyList<string> scopes) =>
        new(true, "VALIDO", "Meta confirmó que el token es válido.", tokenType, expiresAt, scopes);
}

public sealed record MetaMetricDiagnostic(
    string Canal,
    string Nombre,
    string Metrica,
    string Etiqueta,
    bool Configurado,
    bool Ok,
    string Estado,
    long Valor,
    string Mensaje,
    IReadOnlyList<string> Errors)
{
    public static MetaMetricDiagnostic NotConfigured(
        string canal,
        string nombre,
        string metrica,
        string etiqueta,
        string mensaje) =>
        new(canal, nombre, metrica, etiqueta, false, false, "NO_CONFIGURADO", 0, mensaje, []);
}

public sealed record MetaChannelInsight(
    string Canal,
    string Nombre,
    bool Configurado,
    string Estado,
    string Mensaje,
    IReadOnlyList<string> RequisitosFaltantes,
    long Alcance,
    long Impresiones,
    long Interacciones,
    long VisitasPerfil,
    long Clicks,
    long Seguidores,
    IReadOnlyList<string> Errors,
    DateTimeOffset RevisadoEn,
    long VistasSeguidores = 0,
    long VistasNoSeguidores = 0,
    IReadOnlyDictionary<string, long>? Edades = null)
{
    public static MetaChannelInsight NotConfigured(
        string canal,
        string nombre,
        string mensaje,
        IReadOnlyList<string> requisitosFaltantes) =>
        new(canal, nombre, false, "NO_CONFIGURADO", mensaje, requisitosFaltantes, 0, 0, 0, 0, 0, 0, [], DateTimeOffset.UtcNow);
}

public sealed record MetaAudienceBreakdown(
    long FollowerViews,
    long NonFollowerViews,
    IReadOnlyDictionary<string, long> Ages);

public sealed record MetaInsightMetrics(
    IReadOnlyDictionary<string, long> Values,
    IReadOnlyList<string> Errors)
{
    public static MetaInsightMetrics WithError(string error) =>
        new(new Dictionary<string, long>(), [error]);
}
