using CRM.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

using CRM.Data.Data;
using MySql.Data.MySqlClient;

namespace CRM.Data.Services;

public partial class WhatsAppService
{
// =========================================================
// MENSAJE SALIENTE
// =========================================================
//
// Con SendMessagesToMeta=false se conserva la prueba local.
// Con SendMessagesToMeta=true se envía por WhatsApp Cloud API antes
// de guardar el mensaje como enviado.
//
// =========================================================

public async Task<long> ProcesarMensajeSalienteAsync(
    long conversacionId,
    string mensaje,
    long? usuarioId,
    string tipo,
    string? whatsappId,
    long? replyToMessageId = null,
    string? clientRequestId = null)
{
    var requestKey = Guid.TryParse(clientRequestId, out var requestId)
        ? $"{usuarioId}:{requestId:N}"
        : null;
    if (requestKey != null)
    {
        var existente = await _context.Mensajes.AsNoTracking().FirstOrDefaultAsync(m =>
            m.nConversacion == conversacionId && m.cClientRequestId == requestKey);
        if (existente != null) return existente.nMensaje;
    }
    Conversacion? conversacion =
        await _context.Conversaciones
            .Include(c => c.Cliente)
            .FirstOrDefaultAsync(c =>
                c.nConversacion == conversacionId);

    if (conversacion == null)
    {
        throw new InvalidOperationException(
            "La conversación no existe.");
    }

    // =====================================================
    // Si el asesor responde,
    // la conversación queda abierta
    // =====================================================

    conversacion.cEstado = "EN_ATENCION";

    conversacion.dUltimoMensaje =
        DateTime.Now;

    if (usuarioId.HasValue)
    {
        // El modelo usa int?, mientras que
        // el método recibe long?.
        conversacion.nUsuarioAsignado =
            (int)usuarioId.Value;
    }

    if (tipo != "bot")
    {
        conversacion.cBotEstado = "PAUSADO";
        conversacion.dBotPausadoDesde = DateTime.Now;
        conversacion.nBotPausadoPor = usuarioId.HasValue ? (int)usuarioId.Value : null;
    }

    var canal = CanalSocial.Normalizar(conversacion.cCanal);
    string? replyToExternalId = null;
    if (replyToMessageId.HasValue)
    {
        replyToExternalId = await _context.Mensajes
            .Where(m => m.nMensaje == replyToMessageId.Value && m.nConversacion == conversacionId)
            .Select(m => m.cExternalId ?? m.cWhatsappId)
            .FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(replyToExternalId))
        {
            throw new InvalidOperationException("El mensaje seleccionado todavía no puede citarse en WhatsApp.");
        }
    }
    var intentoEnviarAMeta = canal switch
    {
        CanalSocial.WhatsApp => _whatsAppCloudApiService.ShouldSendToMeta,
        CanalSocial.Facebook or CanalSocial.Instagram => _metaMessagingService.PuedeEnviar(canal),
        _ => false
    };

    // =====================================================
    // Crear mensaje
    // =====================================================

    var nuevoMensaje = new Mensaje
    {
        nConversacion =
            conversacion.nConversacion,

        cWhatsappId =
            whatsappId,

        cCanal = canal,

        cExternalId = whatsappId,

        cReplyToExternalId = replyToExternalId,
        cClientRequestId = requestKey,

        cDireccion = 'S',

        cTipo = tipo,

        cEstado = intentoEnviarAMeta
            ? "ENVIANDO"
            : "LOCAL",

        cMensaje = mensaje,

        dFecha = DateTime.Now
    };

    _context.Mensajes.Add(nuevoMensaje);

    // =====================================================
    // GUARDAR
    // =====================================================

    try
    {
        await _context.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (requestKey != null && ex.InnerException is MySqlException { Number: 1062 })
    {
        _context.ChangeTracker.Clear();
        return await _context.Mensajes.AsNoTracking()
            .Where(m => m.nConversacion == conversacionId && m.cClientRequestId == requestKey)
            .Select(m => m.nMensaje).SingleAsync();
    }

    if (intentoEnviarAMeta)
    {
        _ = EnviarMensajeMetaEnSegundoPlanoAsync(
            nuevoMensaje.nMensaje,
            conversacion.Cliente.cTelefono,
            conversacion.cExternalThreadId,
            conversacion.cPhoneNumberId,
            canal,
            mensaje,
            tipo,
            whatsappId,
            replyToExternalId);
    }

    _logger.LogInformation(
        "Mensaje saliente guardado. " +
        "Conversación: {ConversacionId}, " +
        "Mensaje: {MensajeId}",
        conversacionId,
        nuevoMensaje.nMensaje);

    return nuevoMensaje.nMensaje;
}

private async Task EnviarMensajeMetaEnSegundoPlanoAsync(
    long mensajeId,
    string telefono,
    string? externalThreadId,
    string? originPhoneNumberId,
    string canal,
    string mensaje,
    string tipo,
    string? whatsappId,
    string? replyToExternalId)
{
    try
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var cloudApi = scope.ServiceProvider.GetRequiredService<WhatsAppCloudApiService>();
        var r2Storage = scope.ServiceProvider.GetRequiredService<R2StorageService>();
        var metaMessaging = scope.ServiceProvider.GetRequiredService<MetaMessagingService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<WhatsAppService>>();

        var metaId = await EnviarAMetaAsync(
            cloudApi,
            r2Storage,
            metaMessaging,
            canal,
            telefono,
            externalThreadId,
            originPhoneNumberId,
            mensaje,
            tipo,
            whatsappId,
            replyToExternalId);
        var mensajeDb = await context.Mensajes.FirstOrDefaultAsync(m => m.nMensaje == mensajeId);
        if (mensajeDb == null) return;

        mensajeDb.cWhatsappId = metaId;
        mensajeDb.cExternalId = metaId;
        mensajeDb.cEstado = string.IsNullOrWhiteSpace(metaId) ? "LOCAL" : "ENVIADO";
        await context.SaveChangesAsync();

        logger.LogInformation("Mensaje {MensajeId} enviado a Meta en segundo plano.", mensajeId);
    }
    catch (Exception ex)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var mensajeDb = await context.Mensajes.FirstOrDefaultAsync(m => m.nMensaje == mensajeId);
        if (mensajeDb != null)
        {
            mensajeDb.cEstado = $"FALLIDO: {ex.Message}";
            await context.SaveChangesAsync();
        }

        _logger.LogError(ex, "No se pudo enviar el mensaje {MensajeId} a Meta.", mensajeId);
    }
}

private async Task<string?> EnviarAMetaAsync(
    WhatsAppCloudApiService cloudApi,
    R2StorageService r2Storage,
    MetaMessagingService metaMessaging,
    string canal,
    string telefono,
    string? externalThreadId,
    string? originPhoneNumberId,
    string mensaje,
    string tipo,
    string? whatsappId,
    string? replyToExternalId)
{
    if (canal is CanalSocial.Facebook or CanalSocial.Instagram)
    {
        if (!tipo.Equals("text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Por ahora Facebook/Instagram solo envian texto desde el CRM.");
        }

        return await metaMessaging.SendTextMessageAsync(
            canal,
            externalThreadId ?? telefono,
            mensaje);
    }

    if (tipo.Equals("text", StringComparison.OrdinalIgnoreCase))
    {
        return await cloudApi.SendTextMessageAsync(
            telefono,
            mensaje,
            originPhoneNumberId,
            replyToExternalId);
    }

    if (TryReadAttachment(mensaje, out var url, out var nombre, out var objectKey, out var mimeType))
    {
        if (!string.IsNullOrWhiteSpace(objectKey) && !objectKey.StartsWith("local/", StringComparison.OrdinalIgnoreCase))
        {
            var file = await r2Storage.DownloadAsync(objectKey);
            var mediaId = await cloudApi.UploadMediaAsync(
                file.Content,
                string.IsNullOrWhiteSpace(mimeType) ? file.ContentType : mimeType,
                nombre ?? Path.GetFileName(objectKey),
                originPhoneNumberId);
            return await cloudApi.SendMediaByIdAsync(
                telefono,
                tipo,
                mediaId,
                nombre,
                null,
                originPhoneNumberId);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning(
                "No se enviará el adjunto a Meta porque no tiene una URL pública HTTPS. Url={Url}",
                url);
            return whatsappId;
        }

        return await cloudApi.SendMediaMessageAsync(
            telefono,
            tipo,
            url,
            nombre,
            null,
            originPhoneNumberId);
    }

    return whatsappId;
}

private static bool TryReadAttachment(
    string mensaje,
    out string url,
    out string? nombre,
    out string? objectKey,
    out string? mimeType)
{
    url = string.Empty;
    nombre = null;
    objectKey = null;
    mimeType = null;

    try
    {
        using var json = JsonDocument.Parse(mensaje);
        var root = json.RootElement;

        if (root.TryGetProperty("url", out var urlProperty))
        {
            url = urlProperty.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("nombre", out var nombreProperty))
        {
            nombre = nombreProperty.GetString();
        }

        if (root.TryGetProperty("publicId", out var objectKeyProperty))
        {
            objectKey = objectKeyProperty.GetString();
        }

        if (root.TryGetProperty("mimeType", out var mimeTypeProperty))
        {
            mimeType = mimeTypeProperty.GetString();
        }

        return !string.IsNullOrWhiteSpace(url) || !string.IsNullOrWhiteSpace(objectKey);
    }
    catch (JsonException)
    {
        return false;
    }
}
}
