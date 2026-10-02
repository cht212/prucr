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

// =========================================================
// NOTAS INTERNAS
// =========================================================
//
// Antes, la única forma de dejar contexto sobre un cliente
// era como un mensaje de WhatsApp (visible para el cliente).
// Estas notas son privadas del equipo.
//
// =========================================================

[ApiController]
[Route("api/clientes/{clienteId:long}/notas")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class NotasController : ControllerBase
{
    private readonly CrmDbContext _context;
    private readonly CrmAccessService _access;

    public NotasController(CrmDbContext context, CrmAccessService access)
    {
        _context = context;
        _access = access;
    }

    private int? UsuarioActualId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    public async Task<IActionResult> Listar(long clienteId)
    {
        if (!await _access.PuedeAccederClienteAsync(clienteId))
        {
            return Forbid();
        }

        var notas = await _context.NotasInternas
            .AsNoTracking()
            .Where(n => n.nCliente == clienteId)
            .Include(n => n.CreadoPor)
            .OrderByDescending(n => n.dFecha)
            .Select(n => new
            {
                id = n.nNota,
                texto = n.cTexto,
                fecha = n.dFecha,
                autor = n.CreadoPor.cNombre
            })
            .ToListAsync();

        return Ok(notas);
    }

    [HttpPost]
    public async Task<IActionResult> Crear(long clienteId, [FromBody] CrearNotaDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var clienteExiste = await _context.Clientes.AnyAsync(c => c.nCliente == clienteId);
        if (!clienteExiste) return NotFound("Cliente no encontrado.");

        if (!await _access.PuedeAccederClienteAsync(clienteId))
        {
            return Forbid();
        }

        if (dto.ConversacionId.HasValue &&
            !await _access.PuedeAccederConversacionAsync(dto.ConversacionId.Value))
        {
            return Forbid();
        }

        if (dto.ConversacionId.HasValue)
        {
            var conversacionClienteId = await _context.Conversaciones
                .AsNoTracking()
                .Where(conversacion => conversacion.nConversacion == dto.ConversacionId.Value)
                .Select(conversacion => (long?)conversacion.nCliente)
                .FirstOrDefaultAsync();

            if (conversacionClienteId != clienteId)
            {
                return BadRequest("La conversación no pertenece al cliente indicado.");
            }
        }

        var usuarioId = UsuarioActualId;
        if (usuarioId == null) return Unauthorized();

        var nota = new NotaInterna
        {
            nCliente = clienteId,
            nConversacion = dto.ConversacionId,
            cTexto = dto.Texto!.Trim(),
            nCreadoPor = usuarioId.Value,
            dFecha = DateTime.Now
        };

        _context.NotasInternas.Add(nota);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id = nota.nNota });
    }
}

public sealed class CrearNotaDto
{
    public long? ConversacionId { get; set; }

    [Required(ErrorMessage = "El texto de la nota es obligatorio.")]
    [StringLength(4000)]
    public string? Texto { get; set; }
}
