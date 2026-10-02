﻿using System.Text.Json;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/whatsapp/conversaciones")]
[Authorize]
public class WhatsAppAttachmentsController : ControllerBase
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>
        SolicitudesProcesadas = new();
    private static readonly HashSet<string> ExtensionesPermitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> ExtensionesImagen = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    // Antes se guardaba siempre "document", por lo que el front nunca podía
    // distinguir una imagen de un PDF/Word y jamás mostraba una miniatura.
    private static string DeterminarTipo(string extension) =>
        ExtensionesImagen.Contains(extension) ? "image" : "document";

    private const long TamanoMaximo = 15 * 1024 * 1024;
    private readonly WhatsAppService _whatsappService;
    private readonly R2StorageService _storage;
    private readonly CrmAccessService _access;

    public WhatsAppAttachmentsController(
        WhatsAppService whatsappService,
        R2StorageService storage,
        CrmAccessService access)
    {
        _whatsappService = whatsappService;
        _storage = storage;
        _access = access;
    }

    [HttpPost("{conversacionId:long}/archivos")]
    [RequestSizeLimit(TamanoMaximo)]
    public async Task<IActionResult> EnviarArchivo(
        long conversacionId,
        [FromForm] IFormFile archivo,
        [FromForm] string? usuarioId,
        [FromForm] string? clientRequestId,
        CancellationToken cancellationToken)
    {
        if (conversacionId <= 0)
        {
            return BadRequest("La conversación es obligatoria.");
        }

        if (archivo == null || archivo.Length == 0)
        {
            return BadRequest("El archivo es obligatorio.");
        }

        if (archivo.Length > TamanoMaximo)
        {
            return BadRequest("El archivo supera el límite de 15 MB.");
        }

        var extension = Path.GetExtension(archivo.FileName);
        if (!ExtensionesPermitidas.Contains(extension))
        {
            return BadRequest("Tipo de archivo no permitido.");
        }

        if (!TryParseUsuarioId(usuarioId, out var usuarioIdParsed))
        {
            return BadRequest(new { success = false, message = "El usuario no es valido." });
        }

        if (!await _access.PuedeAccederConversacionAsync(conversacionId))
        {
            return Forbid();
        }

        var requestId = Guid.TryParse(clientRequestId, out var parsedRequestId)
            ? parsedRequestId.ToString("N")
            : null;
        var requestKey = requestId == null
            ? null
            : $"{_access.UsuarioActualId}:{conversacionId}:{requestId}";
        if (requestKey != null)
        {
            var expiration = DateTime.UtcNow.AddMinutes(-10);
            foreach (var expired in SolicitudesProcesadas
                         .Where(item => item.Value < expiration)
                         .Select(item => item.Key))
            {
                SolicitudesProcesadas.TryRemove(expired, out _);
            }

            if (!SolicitudesProcesadas.TryAdd(requestKey, DateTime.UtcNow))
            {
                return Conflict(new
                {
                    success = false,
                    duplicate = true,
                    message = "Este archivo ya se está enviando."
                });
            }
        }

        R2UploadResult resultado;
        try
        {
            resultado = await _storage.UploadAsync(archivo, "crm-hpd", cancellationToken);
        }
        catch (InvalidOperationException)
        {
            if (requestKey != null) SolicitudesProcesadas.TryRemove(requestKey, out _);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "El almacenamiento de archivos no está disponible. Intenta nuevamente." });
        }
        var urlPublica = resultado.SecureUrl;
        var contenido = JsonSerializer.Serialize(new
        {
            nombre = Path.GetFileName(archivo.FileName),
            url = urlPublica,
            mimeType = archivo.ContentType,
            tamano = archivo.Length,
            publicId = resultado.PublicId
        });

        long mensajeId;
        try
        {
            mensajeId = await _whatsappService.ProcesarMensajeSalienteAsync(
                conversacionId,
                contenido,
                _access.UsuarioActualId ?? usuarioIdParsed,
                DeterminarTipo(extension),
                null);
        }
        catch
        {
            if (requestKey != null) SolicitudesProcesadas.TryRemove(requestKey, out _);
            throw;
        }

        return Ok(new
        {
            success = true,
            mensajeId,
            url = urlPublica
        });
    }

    private static bool TryParseUsuarioId(string? value, out long? usuarioId)
    {
        usuarioId = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (long.TryParse(value, out var parsed) && parsed > 0)
        {
            usuarioId = parsed;
            return true;
        }

        return false;
    }
}
