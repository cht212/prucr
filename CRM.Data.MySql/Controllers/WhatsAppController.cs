using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace CRM.Data.Controllers
{
    [ApiController]
    [Route("api/whatsapp")]
    [Authorize]
    public class WhatsAppController : ControllerBase
    {
        private readonly WhatsAppService _whatsappService;
        private readonly WhatsAppCloudApiService _whatsAppCloudApiService;
        private readonly R2StorageService _storage;
        private readonly IConfiguration _configuration;
        private readonly SocialIntegrationService _socialIntegrations;
        private readonly CrmAccessService _access;
        private readonly CrmPermissionService _permissions;
        private readonly WhatsAppNumberRegistry _numbers;
        private readonly ILogger<WhatsAppController> _logger;

        public WhatsAppController(
            WhatsAppService whatsappService,
            WhatsAppCloudApiService whatsAppCloudApiService,
            R2StorageService storage,
            IConfiguration configuration,
            SocialIntegrationService socialIntegrations,
            CrmAccessService access,
            CrmPermissionService permissions,
            WhatsAppNumberRegistry numbers,
            ILogger<WhatsAppController> logger)
        {
            _whatsappService = whatsappService;
            _whatsAppCloudApiService = whatsAppCloudApiService;
            _storage = storage;
            _configuration = configuration;
            _socialIntegrations = socialIntegrations;
            _access = access;
            _permissions = permissions;
            _numbers = numbers;
            _logger = logger;
        }


        // =========================================================
        // VERIFICACIÓN DEL WEBHOOK DE META
        // =========================================================
        //
        // Meta llamará a este endpoint cuando configuremos
        // el webhook.
        //
        // URL:
        // GET /api/whatsapp/webhook
        //
        // =========================================================

        [HttpGet("webhook")]
        [AllowAnonymous]
        public IActionResult VerificarWebhook(
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken,
            [FromQuery(Name = "hub.challenge")] string? challenge)
        {
            _logger.LogInformation(
                "Solicitud de verificación de webhook recibida."
            );

            string? tokenConfigurado =
                _socialIntegrations.GetConfiguredValue("WhatsApp:WebhookVerifyToken") ??
                _configuration["WhatsApp:WebhookVerifyToken"];

            if (
                mode == "subscribe" &&
                verifyToken == tokenConfigurado &&
                !string.IsNullOrWhiteSpace(challenge)
            )
            {
                _logger.LogInformation(
                    "Webhook de WhatsApp verificado correctamente."
                );

                return Ok(challenge);
            }

            _logger.LogWarning(
                "Falló la verificación del webhook de WhatsApp."
            );

            return Forbid();
        }


        // =========================================================
        // WEBHOOK DE META
        // =========================================================
        //
        // Meta enviará aquí los mensajes recibidos.
        //
        // POST /api/whatsapp/webhook
        //
        // =========================================================

        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> RecibirWebhook()
        {
            try
            {
                Request.EnableBuffering();
                using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
                var rawBody = await reader.ReadToEndAsync();
                Request.Body.Position = 0;

                if (!ValidarFirmaWebhook(rawBody))
                {
                    return Unauthorized();
                }

                using var payloadDocument = JsonDocument.Parse(rawBody);
                var payload = payloadDocument.RootElement;
                _logger.LogInformation(
                    "Webhook de WhatsApp recibido."
                );


                // -------------------------------------------------
                // Validar estructura básica
                // -------------------------------------------------

                if (!payload.TryGetProperty("entry", out JsonElement entry))
                {
                    _logger.LogWarning(
                        "Webhook recibido sin propiedad 'entry'."
                    );

                    return Ok(new
                    {
                        success = true,
                        message = "Webhook recibido sin mensajes."
                    });
                }


                // -------------------------------------------------
                // entry
                // -------------------------------------------------

                foreach (JsonElement entryItem in entry.EnumerateArray())
                {
                    if (!entryItem.TryGetProperty(
                            "changes",
                            out JsonElement changes))
                    {
                        continue;
                    }


                    // -------------------------------------------------
                    // changes
                    // -------------------------------------------------

                    foreach (JsonElement change in changes.EnumerateArray())
                    {
                        if (!change.TryGetProperty(
                                "value",
                                out JsonElement value))
                        {
                            continue;
                        }


                        // -------------------------------------------------
                        // statuses (acuses de recibo: enviado/entregado/
                        // leído/fallido de nuestros mensajes salientes)
                        // -------------------------------------------------

                        if (value.TryGetProperty(
                                "statuses",
                                out JsonElement statuses))
                        {
                            foreach (JsonElement statusItem in statuses.EnumerateArray())
                            {
                                await ProcesarEstadoMensajeMeta(statusItem);
                            }
                        }


                        // -------------------------------------------------
                        // messages
                        // -------------------------------------------------

                        if (!value.TryGetProperty(
                                "messages",
                                out JsonElement messages))
                        {
                            // Puede ser una notificación que no contiene
                            // un mensaje de cliente.
                            continue;
                        }

                        var originPhoneNumberId = value.TryGetProperty("metadata", out var metadata) &&
                            metadata.TryGetProperty("phone_number_id", out var idNode)
                                ? idNode.GetString() : null;
                        if (!_numbers.Contains(originPhoneNumberId))
                        {
                            _logger.LogWarning("Webhook para Phone Number ID no configurado: {PhoneNumberId}", originPhoneNumberId);
                            continue;
                        }


                        // -------------------------------------------------
                        // contactos
                        // -------------------------------------------------

                        string? nombreContacto = null;

                        if (value.TryGetProperty(
                                "contacts",
                                out JsonElement contacts))
                        {
                            JsonElement firstContact =
                                contacts.EnumerateArray()
                                .FirstOrDefault();

                            if (
                                firstContact.ValueKind !=
                                JsonValueKind.Undefined &&
                                firstContact.TryGetProperty(
                                    "profile",
                                    out JsonElement profile) &&
                                profile.TryGetProperty(
                                    "name",
                                    out JsonElement profileName)
                            )
                            {
                                nombreContacto =
                                    profileName.GetString();
                            }
                        }


                        // -------------------------------------------------
                        // Procesar mensajes
                        // -------------------------------------------------

                        foreach (JsonElement message in messages.EnumerateArray())
                        {
                            await ProcesarMensajeMeta(
                                message,
                                nombreContacto,
                                originPhoneNumberId!
                            );
                        }
                    }
                }


                // -------------------------------------------------
                // IMPORTANTE
                // -------------------------------------------------
                //
                // Respondemos 200 a Meta porque el webhook fue
                // recibido correctamente.
                //
                // -------------------------------------------------

                return Ok(new
                {
                    success = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error procesando webhook de WhatsApp."
                );

                // En caso de error interno devolvemos 500.
                // Así podemos detectar el problema durante desarrollo.
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message = "Error procesando webhook."
                    }
                );
            }
        }


        // =========================================================
        // PROCESAR MENSAJE INDIVIDUAL DE META
        // =========================================================

        private async Task ProcesarMensajeMeta(
            JsonElement message,
            string? nombreContacto,
            string originPhoneNumberId)
        {
            try
            {
                // -------------------------------------------------
                // ID DEL MENSAJE DE WHATSAPP
                // -------------------------------------------------

                string? whatsappId = null;

                if (message.TryGetProperty(
                        "id",
                        out JsonElement id))
                {
                    whatsappId = id.GetString();
                }


                // -------------------------------------------------
                // TELÉFONO DEL CLIENTE
                // -------------------------------------------------

                string? telefono = null;

                if (message.TryGetProperty(
                        "from",
                        out JsonElement from))
                {
                    telefono = from.GetString();
                }


                if (string.IsNullOrWhiteSpace(telefono))
                {
                    _logger.LogWarning(
                        "Mensaje recibido sin número de teléfono."
                    );

                    return;
                }


                // -------------------------------------------------
                // TIPO DE MENSAJE
                // -------------------------------------------------

                string tipo = "unknown";

                if (message.TryGetProperty(
                        "type",
                        out JsonElement type))
                {
                    tipo = type.GetString() ?? "unknown";
                }

                // Meta puede marcar el mensaje como unsupported aunque incluya
                // un bloque descargable. Primero se usan los tipos conocidos y,
                // si no hay coincidencia, se infiere el tipo por mime_type.
                var tieneMedio = TryObtenerMedio(message, ref tipo, out var media);
                if (!tieneMedio && tipo.Equals("unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    var (unsupportedType, errorCode) = ObtenerDetalleUnsupported(message);
                    _logger.LogWarning(
                        "WhatsApp envió un mensaje unsupported sin referencia de medio. MessageId={MessageId}, UnsupportedType={UnsupportedType}, ErrorCode={ErrorCode}",
                        whatsappId,
                        unsupportedType,
                        errorCode);
                }


                // -------------------------------------------------
                // TEXTO
                // -------------------------------------------------

                string texto;
                string tipoGuardado = tipo;
                var replyToExternalId = message.TryGetProperty("context", out var contextNode) &&
                    contextNode.ValueKind == JsonValueKind.Object &&
                    contextNode.TryGetProperty("id", out var replyIdNode) &&
                    replyIdNode.ValueKind == JsonValueKind.String
                        ? replyIdNode.GetString()
                        : null;


                if (
                    tipo == "text" &&
                    message.TryGetProperty(
                        "text",
                        out JsonElement text)
                )
                {
                    texto =
                        text.TryGetProperty(
                            "body",
                            out JsonElement body)
                        ? body.GetString() ?? ""
                        : "";
                }
                else if (tieneMedio)
                {
                    var mediaId = media.TryGetProperty("id", out var mediaIdProperty) &&
                        mediaIdProperty.ValueKind == JsonValueKind.String
                            ? mediaIdProperty.GetString()
                            : null;
                    var mediaUrl = media.TryGetProperty("url", out var mediaUrlProperty) &&
                        mediaUrlProperty.ValueKind == JsonValueKind.String
                            ? mediaUrlProperty.GetString()
                            : null;
                    var mimeTypeHint = media.TryGetProperty("mime_type", out var mimeTypeProperty) &&
                        mimeTypeProperty.ValueKind == JsonValueKind.String
                            ? mimeTypeProperty.GetString()
                            : null;
                    var stickerAnimado = tipo.Equals("sticker", StringComparison.OrdinalIgnoreCase) &&
                        media.TryGetProperty("animated", out var animatedProperty) &&
                        animatedProperty.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                        animatedProperty.GetBoolean();
                    if (tipo.Equals("sticker", StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace(mimeTypeHint) ||
                         mimeTypeHint.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)))
                    {
                        mimeTypeHint = "image/webp";
                    }

                    if (string.IsNullOrWhiteSpace(mediaId) && string.IsNullOrWhiteSpace(mediaUrl))
                    {
                        texto = $"[Mensaje de tipo: {tipo}]";
                    }
                    else
                    {
                        try
                        {
                            var mediaFile = await _whatsAppCloudApiService.DownloadMediaAsync(
                                mediaId,
                                originPhoneNumberId,
                                mediaUrl,
                                mimeTypeHint);
                            var fileName = ObtenerNombreArchivo(
                                message,
                                tipo,
                                mediaFile.ContentType,
                                mediaId ?? "webhook");
                            var upload = await GuardarArchivoEntranteAsync(mediaFile, fileName);
                            texto = JsonSerializer.Serialize(new
                            {
                                nombre = fileName,
                                url = upload.SecureUrl,
                                mimeType = mediaFile.ContentType,
                                tamano = mediaFile.Content.Length,
                                publicId = upload.PublicId,
                                animado = stickerAnimado
                            });
                            tipoGuardado = tipo.ToLowerInvariant() switch
                            {
                                "image" => "image",
                                "sticker" => "sticker",
                                "audio" => "audio",
                                "video" => "video",
                                _ => "document"
                            };
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "No se pudo procesar el archivo entrante de WhatsApp. Tipo: {Tipo}, MediaId: {MediaId}",
                                tipo,
                                mediaId ?? "URL del webhook");
                            tipoGuardado = tipo.ToLowerInvariant() switch
                            {
                                "image" => "image",
                                "sticker" => "sticker",
                                "audio" => "audio",
                                "video" => "video",
                                _ => "document"
                            };
                            texto = JsonSerializer.Serialize(new
                            {
                                nombre = NombreAdjuntoNoDisponible(tipoGuardado),
                                noDisponible = true,
                                tipoOriginal = tipoGuardado,
                                motivo = "Meta no entregó el archivo al CRM."
                            });
                        }
                    }
                }
                else
                {
                    texto = tipo.Equals("unsupported", StringComparison.OrdinalIgnoreCase) ||
                        tipo.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                        ? ObtenerMensajeUnsupported(message)
                        : $"[Mensaje de tipo: {tipo}]";
                }


                // -------------------------------------------------
                // GUARDAR EN CRM
                // -------------------------------------------------

                long conversacionId =
                    await _whatsappService
                    .ProcesarMensajeEntranteAsync(
                        telefono,
                        nombreContacto,
                        texto,
                        tipoGuardado,
                        whatsappId,
                        originPhoneNumberId,
                        replyToExternalId
                    );


                _logger.LogInformation(
                    "Mensaje de WhatsApp procesado. " +
                    "Conversación: {ConversacionId}, " +
                    "Teléfono: {Telefono}",
                    conversacionId,
                    telefono
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error procesando mensaje individual de WhatsApp."
                );

                throw;
            }
        }

        // =========================================================
        // PROCESAR ACUSE DE ESTADO (statuses) DE META
        // =========================================================
        //
        // Meta manda aquí sent / delivered / read / failed para cada
        // mensaje saliente que enviamos, identificado por su
        // whatsappId (message.id). Actualizamos el campo c_estado
        // del mensaje correspondiente para pintar los checks en el
        // chat (check enviado, doble check entregado, doble check azul leído, x fallido).
        //
        // =========================================================

        private async Task ProcesarEstadoMensajeMeta(JsonElement statusItem)
        {
            try
            {
                string? whatsappId = statusItem.TryGetProperty("id", out var idProp)
                    ? idProp.GetString()
                    : null;

                string? estadoMeta = statusItem.TryGetProperty("status", out var statusProp)
                    ? statusProp.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(whatsappId) || string.IsNullOrWhiteSpace(estadoMeta))
                {
                    return;
                }

                string? detalleError = null;

                if (estadoMeta.Equals("failed", StringComparison.OrdinalIgnoreCase) &&
                    statusItem.TryGetProperty("errors", out var errors))
                {
                    var primerError = errors.EnumerateArray().FirstOrDefault();
                    if (primerError.ValueKind != JsonValueKind.Undefined &&
                        primerError.TryGetProperty("title", out var titulo))
                    {
                        detalleError = titulo.GetString();
                    }
                }

                await _whatsappService.ActualizarEstadoMensajeAsync(
                    whatsappId,
                    estadoMeta,
                    detalleError);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error procesando acuse de estado (statuses) de WhatsApp.");
            }
        }

        private bool ValidarFirmaWebhook(string rawBody)
        {
            var appSecret = _socialIntegrations.GetConfiguredValue("WhatsApp:AppSecret") ??
                _configuration["WhatsApp:AppSecret"];
            if (string.IsNullOrWhiteSpace(appSecret))
            {
                _logger.LogError("WhatsApp:AppSecret no está configurado; se rechaza el webhook por seguridad.");
                return false;
            }

            var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(signature) || !signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var expected = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(signature));
        }

        private static string ObtenerNombreArchivo(JsonElement message, string type, string contentType, string mediaId)
        {
            if (message.TryGetProperty(type, out var media) &&
                media.TryGetProperty("filename", out var filename))
            {
                var suppliedName = Path.GetFileName(filename.GetString());
                if (!string.IsNullOrWhiteSpace(suppliedName)) return suppliedName;
            }

            var mimeType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
            var extension = mimeType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                "audio/ogg" => ".ogg",
                "audio/mpeg" => ".mp3",
                "audio/aac" => ".aac",
                "video/mp4" => ".mp4",
                "application/pdf" => ".pdf",
                _ => ".bin"
            };
            return $"whatsapp-{mediaId}{extension}";
        }

        private static bool TryObtenerMedio(JsonElement message, ref string type, out JsonElement media)
        {
            foreach (var mediaType in new[] { "image", "sticker", "document", "audio", "video" })
            {
                if (message.TryGetProperty(mediaType, out var candidate) &&
                    TieneReferenciaDeMedio(candidate))
                {
                    type = mediaType;
                    media = candidate;
                    return true;
                }
            }

            foreach (var property in message.EnumerateObject())
            {
                var candidate = property.Value;
                if (!TieneReferenciaDeMedio(candidate) ||
                    !candidate.TryGetProperty("mime_type", out var mimeNode) ||
                    mimeNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var mimeType = mimeNode.GetString() ?? string.Empty;
                type = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image"
                    : mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ? "audio"
                    : mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? "video"
                    : "document";
                media = candidate;
                return true;
            }

            media = default;
            return false;
        }

        private static bool TieneReferenciaDeMedio(JsonElement media)
        {
            if (media.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var tieneId = media.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString());
            if (tieneId)
            {
                return true;
            }

            return media.TryGetProperty("url", out var url) &&
                url.ValueKind == JsonValueKind.String &&
                Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                uri.Host.Equals("lookaside.fbsbx.com", StringComparison.OrdinalIgnoreCase);
        }

        private static (string? Type, int? ErrorCode) ObtenerDetalleUnsupported(JsonElement message)
        {
            var unsupportedType = message.TryGetProperty("unsupported", out var unsupported) &&
                unsupported.ValueKind == JsonValueKind.Object &&
                unsupported.TryGetProperty("type", out var typeNode) &&
                typeNode.ValueKind == JsonValueKind.String
                    ? typeNode.GetString()
                    : null;
            int? errorCode = null;
            if (message.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                var firstError = errors.EnumerateArray().FirstOrDefault();
                if (firstError.ValueKind == JsonValueKind.Object &&
                    firstError.TryGetProperty("code", out var codeNode) &&
                    codeNode.TryGetInt32(out var code))
                {
                    errorCode = code;
                }
            }

            return (unsupportedType, errorCode);
        }

        private static string ObtenerMensajeUnsupported(JsonElement message)
        {
            var (unsupportedType, errorCode) = ObtenerDetalleUnsupported(message);
            if (errorCode == 131051)
            {
                return "Contenido de WhatsApp no disponible. Pídele al cliente que lo reenvíe como imagen, video o sticker estático.";
            }

            if (string.Equals(unsupportedType, "gif", StringComparison.OrdinalIgnoreCase))
            {
                return "GIF no disponible. Pídele al cliente que lo reenvíe como sticker o video.";
            }

            if (errorCode == 131060)
            {
                return "Contenido de WhatsApp no disponible. Pídele al cliente que lo reenvíe.";
            }

            var detalle = string.IsNullOrWhiteSpace(unsupportedType)
                ? "tipo no compatible"
                : unsupportedType;
            return $"Contenido de WhatsApp no disponible ({detalle}). Pídele al cliente que lo reenvíe.";
        }

        private static string NombreAdjuntoNoDisponible(string tipo) => tipo.ToLowerInvariant() switch
        {
            "audio" or "voice" => "Audio no disponible",
            "sticker" => "Sticker no disponible",
            "image" => "Imagen no disponible",
            "video" => "Video no disponible",
            _ => "Archivo no disponible"
        };

        private async Task<R2UploadResult> GuardarArchivoEntranteAsync(
            WhatsAppMediaDownload mediaFile,
            string fileName)
        {
            await using var stream = new MemoryStream(mediaFile.Content);
            var formFile = new FormFile(stream, 0, mediaFile.Content.Length, "archivo", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = mediaFile.ContentType
            };

            try
            {
                return await _storage.UploadAsync(formFile, "crm-hpd");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Cloudflare R2 no pudo guardar el archivo entrante. Se usará almacenamiento privado local. Archivo: {FileName}",
                    fileName);

                return await _storage.SaveIncomingLocalAsync(mediaFile.Content, fileName, mediaFile.ContentType);
            }
        }


        // =========================================================

        [HttpGet("conversaciones/{conversacionId:long}")]
        public async Task<IActionResult> Conversacion(long conversacionId, [FromQuery] long? antesDe = null, [FromQuery] int pageSize = 50)
        {
            if (!_access.UsuarioActualId.HasValue) return Unauthorized();
            var permisos = await ObtenerPermisosEfectivosAsync();
            var verTodas = permisos.Contains(CrmPermissionService.ViewAllChats);
            var resultado = await _whatsappService.ObtenerConversacionAsync(
                conversacionId,
                verTodas ? null : _access.UsuarioActualId,
                !verTodas,
                CrmPermissionService.GetAllowedChannels(permisos),
                permisos.Contains(CrmPermissionService.ViewCustomerDetails) ||
                permisos.Contains(CrmPermissionService.EditConversationContact),
                pageSize, antesDe, _access.UsuarioActualId, _access.TieneAccesoGlobal && verTodas);
            if (resultado == null)
            {
                return NotFound(new { message = "Conversacion no encontrada." });
            }

            return Ok(resultado);
        }

        [HttpGet("conversaciones")]
        public async Task<IActionResult> Todas([FromQuery] int page = 1, [FromQuery] int pageSize = 50,
            [FromQuery] string? canal = null, [FromQuery] string? search = null,
            [FromQuery] string? filtro = null, [FromQuery] string? asesor = null)
        {
            if (!_access.UsuarioActualId.HasValue) return Unauthorized();
            var permisos = await ObtenerPermisosEfectivosAsync();
            var verTodas = permisos.Contains(CrmPermissionService.ViewAllChats);
            var conversaciones = await _whatsappService.ObtenerTodasConversacionesAsync(
                verTodas ? null : _access.UsuarioActualId,
                !verTodas,
                CrmPermissionService.GetAllowedChannels(permisos), page, pageSize, canal, search, filtro,
                verTodas ? asesor : null, _access.UsuarioActualId);
            return Ok(conversaciones);
        }

        private async Task<HashSet<string>> ObtenerPermisosEfectivosAsync() =>
            _access.UsuarioActualId.HasValue
                ? await _permissions.GetEffectiveAsync(_access.UsuarioActualId.Value, _access.RolActual)
                : [];

        [HttpPost("conversaciones/{conversacionId:long}/escribiendo")]
        public async Task<IActionResult> MostrarEscribiendo(long conversacionId)
        {
            if (!await _access.PuedeAccederConversacionAsync(conversacionId))
            {
                return Forbid();
            }

            var enviado = await _whatsappService.MostrarEscribiendoAsync(conversacionId);
            return Ok(new { success = enviado });
        }

        [HttpPost("conversaciones/{conversacionId:long}/mensajes")]
        public async Task<IActionResult> EnviarMensaje(long conversacionId, [FromBody] EnviarMensajeDto dto)
        {
            if (conversacionId <= 0)
            {
                return BadRequest("La conversacion es obligatoria.");
            }

            if (string.IsNullOrWhiteSpace(dto.Mensaje))
            {
                return BadRequest("El mensaje es obligatorio.");
            }
            if (dto.ClientRequestId != null && !Guid.TryParse(dto.ClientRequestId, out _))
                return BadRequest("La referencia del envio no es valida.");

            if (!await _access.PuedeAccederConversacionAsync(conversacionId))
            {
                return Forbid();
            }

            var mensajeId = await _whatsappService.ProcesarMensajeSalienteAsync(
                conversacionId,
                dto.Mensaje,
                _access.UsuarioActualId,
                dto.Tipo ?? "text",
                null,
                dto.ReplyToMessageId,
                dto.ClientRequestId);

            return Ok(new { success = true, mensajeId });
        }

        [HttpPut("conversaciones/{conversacionId:long}/bot")]
        public async Task<IActionResult> CambiarEstadoBot(long conversacionId, [FromBody] CambiarBotDto dto)
        {
            if (conversacionId <= 0)
            {
                return BadRequest("La conversacion es obligatoria.");
            }

            if (!await _access.PuedeControlarBotConversacionAsync(conversacionId))
            {
                return Forbid();
            }

            var usuarioId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
                ? id
                : (int?)null;

            await _whatsappService.CambiarEstadoBotConversacionAsync(
                conversacionId,
                dto.Estado ?? "PAUSADO",
                usuarioId);

            return Ok(new { success = true, estado = dto.Estado });
        }

        [HttpPost("conversaciones/asignar-pendientes")]
        [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
        public async Task<IActionResult> AsignarConversacionesPendientes()
        {
            var cantidad = await _whatsappService.AsignarConversacionesPendientesAsync();
            return Ok(new
            {
                success = true,
                asignadas = cantidad,
                mensaje = cantidad > 0
                    ? $"Se asignaron {cantidad} conversaciones pendientes."
                    : "No había conversaciones pendientes por asignar."
            });
        }
    }

    public class EnviarMensajeDto
    {
        public string? ClientRequestId { get; set; }
        public string Mensaje { get; set; } = "";

        public long? UsuarioId { get; set; }

        public string? Tipo { get; set; }

        public long? ReplyToMessageId { get; set; }
    }

    public class CambiarBotDto
    {
        public string? Estado { get; set; }
    }
}
