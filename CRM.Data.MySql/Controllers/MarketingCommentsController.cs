using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using CRM.Data.Data;
using CRM.Data.Models;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/crm/comentarios")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public sealed class MarketingCommentsController : ControllerBase
{
    private readonly CrmDbContext _context;
    private readonly MetaMessagingService _metaMessaging;
    private readonly AuditoriaService _auditoria;

    public MarketingCommentsController(
        CrmDbContext context,
        MetaMessagingService metaMessaging,
        AuditoriaService auditoria)
    {
        _context = context;
        _metaMessaging = metaMessaging;
        _auditoria = auditoria;
    }

    private int? UsuarioActualId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? canal = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 100 : pageSize;

        var query = _context.Mensajes.AsNoTracking()
            .Where(mensaje => mensaje.cTipo == "comment");
        if (!string.IsNullOrWhiteSpace(canal) &&
            !canal.Equals("TODOS", StringComparison.OrdinalIgnoreCase))
        {
            var canalNormalizado = CanalSocial.Normalizar(canal);
            query = query.Where(mensaje => mensaje.cCanal == canalNormalizado);
        }

        var total = await query.CountAsync();
        var items = await query
            .Include(mensaje => mensaje.Conversacion)
            .ThenInclude(conversacion => conversacion.Cliente)
            .OrderByDescending(mensaje => mensaje.dFecha)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(mensaje => new
            {
                id = mensaje.nMensaje,
                canal = mensaje.cCanal,
                texto = (string?)mensaje.cMensaje,
                estado = mensaje.cEstado,
                fecha = mensaje.dFecha,
                externalId = mensaje.cExternalId,
                publicacionId = mensaje.cReplyToExternalId,
                respuestas = _context.Mensajes
                    .Where(respuesta =>
                        respuesta.nConversacion == mensaje.nConversacion &&
                        respuesta.cTipo == "comment_reply" &&
                        respuesta.cReplyToExternalId == mensaje.cExternalId)
                    .OrderBy(respuesta => respuesta.dFecha)
                    .Select(respuesta => new
                    {
                        id = respuesta.nMensaje,
                        texto = respuesta.cMensaje,
                        estado = respuesta.cEstado,
                        fecha = respuesta.dFecha,
                        externalId = respuesta.cExternalId
                    })
                    .ToList(),
                cliente = new
                {
                    nombre = mensaje.Conversacion.Cliente.cNombre
                }
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost("{id:long}/respuestas")]
    [CrmPermission(CrmPermissionService.ManageMarketing)]
    public async Task<IActionResult> Responder(long id, [FromBody] ResponderComentarioDto dto)
    {
        var texto = dto.Texto?.Trim();
        if (string.IsNullOrWhiteSpace(texto))
        {
            return BadRequest(new { message = "La respuesta es obligatoria." });
        }

        if (texto.Length > 1000)
        {
            return BadRequest(new { message = "La respuesta no puede superar 1000 caracteres." });
        }

        var comentario = await _context.Mensajes
            .FirstOrDefaultAsync(mensaje => mensaje.nMensaje == id && mensaje.cTipo == "comment");
        if (comentario == null)
        {
            return NotFound(new { message = "Comentario no encontrado." });
        }

        if (string.IsNullOrWhiteSpace(comentario.cExternalId))
        {
            return BadRequest(new
            {
                message = "El comentario no tiene identificador externo y no puede responderse en la red social."
            });
        }

        string? externalId;
        try
        {
            externalId = await _metaMessaging.ReplyToPublicCommentAsync(
                comentario.cCanal,
                comentario.cExternalId,
                texto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        var respuesta = new Mensaje
        {
            nConversacion = comentario.nConversacion,
            cCanal = comentario.cCanal,
            cExternalId = externalId,
            cReplyToExternalId = comentario.cExternalId,
            cDireccion = 'S',
            cTipo = "comment_reply",
            cEstado = "ENVIADO",
            cMensaje = texto,
            dFecha = DateTime.Now
        };
        _context.Mensajes.Add(respuesta);
        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync(
            "Comentario",
            comentario.nMensaje,
            "RESPUESTA_PUBLICA",
            null,
            texto,
            UsuarioActualId);

        return Ok(new
        {
            success = true,
            id = respuesta.nMensaje,
            externalId,
            texto = respuesta.cMensaje,
            fecha = respuesta.dFecha
        });
    }
}

public sealed class ResponderComentarioDto
{
    [Required]
    [MaxLength(1000)]
    public string Texto { get; set; } = string.Empty;
}
