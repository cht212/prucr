using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CRM.Data.Models;

namespace CRM.Data.Services;

public sealed record SocialPublication(string Canal, string Id, DateTimeOffset PublicadoEn,
    string Texto, string? Url, string? ImagenUrl, long MeGusta, long Comentarios,
    long Compartidos, long? Visualizaciones, string? Tipo,
    IReadOnlyDictionary<string, long>? Reacciones = null);

public sealed record SocialPublicationChannel(string Canal, bool Configured, string? Error,
    bool Partial, IReadOnlyList<SocialPublication> Posts, string? CommentsError = null);

public sealed record SocialPublicationDay(DateOnly Fecha, string Canal, int Publicaciones,
    long MeGusta, long Comentarios, long Compartidos, long Visualizaciones);

public sealed record SocialPublicationReport(DateOnly Desde, DateOnly Hasta,
    IReadOnlyList<SocialPublicationChannel> Canales, IReadOnlyList<SocialPublicationDay> Serie);

public sealed class SocialPublicationsService
{
    private readonly HttpClient _http;
    private readonly SocialIntegrationService _settings;
    private readonly MetaGraphApiService _meta;
    private readonly SocialInboundService _inbound;
    private readonly SemaphoreSlim _databaseSync = new(1, 1);

    public SocialPublicationsService(
        HttpClient http,
        SocialIntegrationService settings,
        MetaGraphApiService meta,
        SocialInboundService inbound)
    {
        _http = http;
        _settings = settings;
        _meta = meta;
        _inbound = inbound;
    }

    public async Task<SocialPublicationReport> GetReportAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var channels = await Task.WhenAll(
            FacebookAsync(desde, hasta, ct),
            InstagramAsync(desde, hasta, ct),
            TikTokAsync(desde, hasta, ct));
        var series = channels.SelectMany(c => c.Posts)
            .GroupBy(p => new { Fecha = DateOnly.FromDateTime(p.PublicadoEn.UtcDateTime), p.Canal })
            .Select(g => new SocialPublicationDay(g.Key.Fecha, g.Key.Canal, g.Count(),
                g.Sum(p => p.MeGusta), g.Sum(p => p.Comentarios),
                g.Sum(p => p.Compartidos), g.Sum(p => p.Visualizaciones ?? 0)))
            .OrderBy(x => x.Fecha).ThenBy(x => x.Canal).ToArray();
        return new SocialPublicationReport(desde, hasta, channels, series);
    }

    private async Task<SocialPublicationChannel> FacebookAsync(
        DateOnly desde,
        DateOnly hasta,
        CancellationToken ct)
    {
        var result = await _meta.ObtenerFacebookFeedAsync(desde.ToDateTime(TimeOnly.MinValue),
            hasta.ToDateTime(TimeOnly.MinValue), 25, ct);
        var posts = result.Posts.Select(p => new SocialPublication(CanalSocial.Facebook, p.Id,
            DateTimeOffset.TryParse(p.CreatedTime, out var at) ? at : DateTimeOffset.MinValue,
            p.Message, p.PermalinkUrl, p.MediaUrl, p.Likes, p.Comments, p.Shares,
            p.Impressions > 0 ? p.Impressions : null,
            p.MediaType,
            p.ReactionsByType))
            .Where(p => InRange(p.PublicadoEn, desde, hasta)).ToArray();
        var commentsError = await SyncCommentsAsync(posts, ct);
        return new SocialPublicationChannel(CanalSocial.Facebook,
            _settings.GetConfiguredValue("Meta:Facebook:PageId") != null,
            result.Error, result.Posts.Count >= 25, posts, commentsError);
    }

    private async Task<SocialPublicationChannel> InstagramAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var id = _settings.GetConfiguredValue("Meta:Instagram:InstagramBusinessAccountId") ??
            _settings.GetConfiguredValue("Meta:Instagram:LoginUserId");
        var token = _settings.GetConfiguredValue("Meta:Instagram:LoginAccessToken") ??
            _settings.GetConfiguredValue("Meta:Instagram:AccessToken");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(token))
            return new(CanalSocial.Instagram, false,
                "Configura Instagram Professional User ID y el Instagram Login Access Token.", false, []);

        var posts = new List<SocialPublication>();
        string? after = null;
        var partial = false;
        using var metricConcurrency = new SemaphoreSlim(5);
        try
        {
            for (var page = 0; page < 5; page++)
            {
                var version = _settings.GetConfiguredValue("Meta:ApiVersion") ?? "v25.0";
                var url = $"https://graph.instagram.com/{version}/{Uri.EscapeDataString(id)}/media" +
                    "?fields=id,caption,media_type,media_product_type,media_url,thumbnail_url,permalink,timestamp,like_count,comments_count&limit=25" +
                    (after == null ? "" : $"&after={Uri.EscapeDataString(after)}");
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await _http.SendAsync(request, ct);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!response.IsSuccessStatusCode)
                    return new(CanalSocial.Instagram, true, ApiError(doc.RootElement), partial, posts);
                var root = doc.RootElement;
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) break;
                var oldest = DateTimeOffset.MaxValue;
                var candidates = new List<(JsonElement Item, DateTimeOffset PublishedAt)>();
                foreach (var item in data.EnumerateArray())
                {
                    if (!DateTimeOffset.TryParse(Str(item, "timestamp"), out var at)) continue;
                    if (at < oldest) oldest = at;
                    if (!InRange(at, desde, hasta)) continue;
                    candidates.Add((item.Clone(), at));
                }

                var pagePosts = await Task.WhenAll(candidates.Select(async candidate =>
                {
                    var item = candidate.Item;
                    var mediaId = Str(item, "id") ?? string.Empty;
                    await metricConcurrency.WaitAsync(ct);
                    try
                    {
                        var mediaMetrics = await GetInstagramMediaMetricsAsync(mediaId, token, version, ct);
                        return new SocialPublication(CanalSocial.Instagram, mediaId, candidate.PublishedAt,
                            Str(item, "caption") ?? "", Str(item, "permalink"),
                            Str(item, "media_url") ?? Str(item, "thumbnail_url"),
                            Num(item, "like_count"), Num(item, "comments_count"),
                            mediaMetrics.Shares, mediaMetrics.Views,
                            Str(item, "media_product_type") ?? Str(item, "media_type"));
                    }
                    finally
                    {
                        metricConcurrency.Release();
                    }
                }));
                posts.AddRange(pagePosts);
                after = root.TryGetProperty("paging", out var paging) &&
                    paging.TryGetProperty("cursors", out var cursors) ? Str(cursors, "after") : null;
                if (after == null || oldest.UtcDateTime.Date < desde.ToDateTime(TimeOnly.MinValue)) break;
                partial = page == 4;
            }
            var commentsError = await SyncCommentsAsync(posts, ct);
            return new(CanalSocial.Instagram, true, null, partial, posts, commentsError);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(CanalSocial.Instagram, true, "No se pudo consultar Instagram: " + ex.Message, partial, posts);
        }
    }

    private async Task<(long? Views, long Shares)> GetInstagramMediaMetricsAsync(
        string mediaId,
        string token,
        string apiVersion,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mediaId)) return (null, 0);

        var url = $"https://graph.instagram.com/{apiVersion}/{Uri.EscapeDataString(mediaId)}/insights" +
            "?metric=views,shares";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return (null, 0);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return (null, 0);
            }

            long? views = null;
            long shares = 0;
            foreach (var metric in data.EnumerateArray())
            {
                var name = Str(metric, "name");
                var value = InsightValue(metric);
                if (string.Equals(name, "views", StringComparison.OrdinalIgnoreCase)) views = value;
                if (string.Equals(name, "shares", StringComparison.OrdinalIgnoreCase)) shares = value;
            }

            return (views, shares);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, 0);
        }
    }

    private static long InsightValue(JsonElement metric)
    {
        if (metric.TryGetProperty("total_value", out var totalValue) &&
            totalValue.ValueKind == JsonValueKind.Object &&
            totalValue.TryGetProperty("value", out var total))
        {
            return ReadLong(total);
        }

        if (metric.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            return values.EnumerateArray()
                .Where(value => value.TryGetProperty("value", out _))
                .Sum(value => ReadLong(value.GetProperty("value")));
        }

        return 0;
    }

    private static long ReadLong(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private async Task<SocialPublicationChannel> TikTokAsync(DateOnly desde, DateOnly hasta, CancellationToken ct)
    {
        var token = _settings.GetConfiguredValue("TikTok:DisplayAccessToken");
        if (string.IsNullOrWhiteSpace(token))
            return new(CanalSocial.TikTok, false,
                "Configura el token de TikTok Display API con permiso video.list.", false, []);

        var posts = new List<SocialPublication>();
        long? cursor = null;
        var partial = false;
        try
        {
            for (var page = 0; page < 5; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://open.tiktokapis.com/v2/video/list/?fields=id,create_time,title,video_description,cover_image_url,share_url,like_count,comment_count,share_count,view_count");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(JsonSerializer.Serialize(new { max_count = 20, cursor }),
                    Encoding.UTF8, "application/json");
                using var response = await _http.SendAsync(request, ct);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                if (!response.IsSuccessStatusCode ||
                    (root.TryGetProperty("error", out var err) && Str(err, "code") != "ok"))
                    return new(CanalSocial.TikTok, true, ApiError(root), partial, posts);
                if (!root.TryGetProperty("data", out var data) || !data.TryGetProperty("videos", out var videos)) break;
                var oldest = DateTimeOffset.MaxValue;
                foreach (var item in videos.EnumerateArray())
                {
                    var at = DateTimeOffset.FromUnixTimeSeconds(Num(item, "create_time"));
                    if (at < oldest) oldest = at;
                    if (!InRange(at, desde, hasta)) continue;
                    posts.Add(new(CanalSocial.TikTok, Str(item, "id") ?? "", at,
                        Str(item, "video_description") ?? Str(item, "title") ?? "",
                        Str(item, "share_url"), Str(item, "cover_image_url"),
                        Num(item, "like_count"), Num(item, "comment_count"),
                        Num(item, "share_count"), Num(item, "view_count"), "VIDEO"));
                }
                if (!data.TryGetProperty("has_more", out var more) || more.ValueKind != JsonValueKind.True ||
                    !data.TryGetProperty("cursor", out var next) || !next.TryGetInt64(out var nextCursor) ||
                    oldest.UtcDateTime.Date < desde.ToDateTime(TimeOnly.MinValue)) break;
                cursor = nextCursor;
                partial = page == 4;
            }
            return new(CanalSocial.TikTok, true, null, partial, posts);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or ArgumentOutOfRangeException)
        {
            return new(CanalSocial.TikTok, true, "No se pudo consultar TikTok: " + ex.Message, partial, posts);
        }
    }

    private async Task<string?> SyncCommentsAsync(
        IEnumerable<SocialPublication> publications,
        CancellationToken cancellationToken)
    {
        var withComments = publications
            .Where(publication => publication.Comentarios > 0 && !string.IsNullOrWhiteSpace(publication.Id))
            .ToArray();
        if (withComments.Length == 0) return null;

        using var concurrency = new SemaphoreSlim(5);
        var commentGroups = await Task.WhenAll(withComments.Select(async publication =>
        {
            var acquired = false;
            try
            {
                await concurrency.WaitAsync(cancellationToken);
                acquired = true;

                return await _meta.ObtenerComentariosPublicacionAsync(
                    publication.Canal,
                    publication.Id,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return MetaPublicationCommentsResult.Failed("La sincronización de comentarios fue cancelada.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                return MetaPublicationCommentsResult.Failed(ex.Message);
            }
            finally
            {
                if (acquired) concurrency.Release();
            }
        }));

        if (cancellationToken.IsCancellationRequested)
            return "La sincronización de comentarios fue cancelada.";

        try
        {
            await PersistCommentsAsync(commentGroups.SelectMany(group => group.Comments), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return "La sincronización de comentarios fue cancelada.";
        }

        return commentGroups.FirstOrDefault(group => !group.Success)?.Error;
    }

    public async Task<MetaPublicationCommentsResult> SyncPublicationCommentsAsync(
        string canal,
        string publicationId,
        CancellationToken cancellationToken)
    {
        var result = await _meta.ObtenerComentariosPublicacionAsync(
            canal,
            publicationId,
            cancellationToken);
        if (!result.Success || result.Comments.Count == 0)
        {
            return result;
        }

        await PersistCommentsAsync(result.Comments, cancellationToken);
        return result;
    }

    private async Task PersistCommentsAsync(
        IEnumerable<MetaPublicationComment> comments,
        CancellationToken cancellationToken)
    {
        await _databaseSync.WaitAsync(cancellationToken);
        try
        {
            var perfiles = new Dictionary<string, MetaContactProfile?>(StringComparer.OrdinalIgnoreCase);
            foreach (var comment in comments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nombre = comment.Username;
                string? fotoPerfil = null;
                if (EsNombreGenericoMeta(nombre, comment.Canal) &&
                    !comment.UserId.StartsWith("comment:", StringComparison.OrdinalIgnoreCase))
                {
                    var profileKey = $"{comment.Canal}:{comment.UserId}";
                    if (!perfiles.TryGetValue(profileKey, out var perfil))
                    {
                        var profileResult = await _meta.ObtenerPerfilContactoDetalladoAsync(
                            comment.Canal,
                            comment.UserId, cancellationToken: cancellationToken);
                        perfil = profileResult.Profile;
                        perfiles[profileKey] = perfil;
                    }

                    if (perfil != null)
                    {
                        nombre = comment.Canal == CanalSocial.Instagram
                            ? perfil.Username ?? perfil.DisplayName
                            : perfil.DisplayName ?? perfil.Username;
                        fotoPerfil = perfil.ProfilePictureUrl;
                    }
                }

                await _inbound.RegistrarMensajeEntranteAsync(new SocialInboundMessage(
                    comment.Canal,
                    comment.UserId,
                    comment.UserId,
                    nombre ?? (comment.Canal == CanalSocial.Instagram ? "Instagram" : "Facebook"),
                    $"Comentario: {comment.Text}",
                    "comment",
                    comment.Id,
                    fotoPerfil,
                    false,
                    comment.PublicationId,
                    comment.CreatedAt));
            }
        }
        finally
        {
            _databaseSync.Release();
        }
    }

    private static bool EsNombreGenericoMeta(string? nombre, string canal) =>
        string.IsNullOrWhiteSpace(nombre) ||
        nombre.Equals(canal, StringComparison.OrdinalIgnoreCase) ||
        nombre.Equals("Facebook", StringComparison.OrdinalIgnoreCase) ||
        nombre.Equals("Instagram", StringComparison.OrdinalIgnoreCase) ||
        nombre.Equals("Usuario de Facebook", StringComparison.OrdinalIgnoreCase) ||
        nombre.Equals("Usuario de Instagram", StringComparison.OrdinalIgnoreCase);

    private static bool InRange(DateTimeOffset at, DateOnly from, DateOnly to) =>
        DateOnly.FromDateTime(at.UtcDateTime) is var day && day >= from && day <= to;

    private static string? Str(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long Num(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) &&
        value.TryGetInt64(out var number) ? number : 0;

    private static string ApiError(JsonElement root) =>
        root.TryGetProperty("error", out var error) && Str(error, "message") is { } message
            ? message : "La API rechazo la consulta. Revisa el token y los permisos.";
}
