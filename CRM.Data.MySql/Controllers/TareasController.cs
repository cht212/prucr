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
// TAREAS (recordatorios / seguimientos)
// =========================================================
//
// No existía ninguna forma de programar un seguimiento
// ("llamar el jueves", "enviar cotización mañana"). Este
// controlador cubre ese vacío: tareas ligadas opcionalmente
// a un cliente, conversación u oportunidad, asignables a un
// asesor y con fecha de vencimiento.
//
// =========================================================

[ApiController]
[Route("api/tareas")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class TareasController : ControllerBase
{
    private readonly CrmDbContext _context;
    private readonly CrmAccessService _access;

    public TareasController(CrmDbContext context, CrmAccessService access)
    {
        _context = context;
        _access = access;
    }

    private int? UsuarioActualId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    // Por defecto solo trae PENDIENTE, y marca como VENCIDA (en la
    // respuesta, sin persistir) las que ya pasaron su fecha límite.
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] int? usuarioId = null,
        [FromQuery] long? clienteId = null,
        [FromQuery] long? conversacionId = null,
        [FromQuery] string? estado = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? filtro = null,
        [FromQuery] bool soloMias = false)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = _access.FiltrarTareas(_context.Tareas.AsNoTracking());
        if (usuarioId.HasValue && _access.TieneAccesoGlobal) query = query.Where(t => t.nAsignadoA == usuarioId.Value);
        if (clienteId.HasValue) query = query.Where(t => t.nCliente == clienteId.Value);
        if (conversacionId.HasValue) query = query.Where(t => t.nConversacion == conversacionId.Value);
        if (!string.IsNullOrWhiteSpace(estado))
        {
            query = query.Where(t => t.cEstado == estado.Trim().ToUpperInvariant());
        }

        var ahora = DateTime.Now;
        var hoy = ahora.Date;
        var manana = hoy.AddDays(1);
        if (soloMias) query = query.Where(t => t.nAsignadoA == UsuarioActualId);
        var resumen = await query.GroupBy(_ => 1).Select(g => new
        {
            total = g.Count(t => t.cEstado != "CANCELADA"),
            pendientes = g.Count(t => t.cEstado == "PENDIENTE" && t.dFechaVencimiento >= ahora),
            vencidas = g.Count(t => t.cEstado == "PENDIENTE" && t.dFechaVencimiento < ahora),
            hoy = g.Count(t => t.cEstado == "PENDIENTE" && t.dFechaVencimiento >= hoy && t.dFechaVencimiento < manana),
            completadas = g.Count(t => t.cEstado == "COMPLETADA"),
            mias = g.Count(t => t.nAsignadoA == UsuarioActualId && t.cEstado != "COMPLETADA" && t.cEstado != "CANCELADA")
        }).FirstOrDefaultAsync();
        query = filtro switch
        {
            "mias" => query.Where(t => t.nAsignadoA == UsuarioActualId && t.cEstado != "COMPLETADA" && t.cEstado != "CANCELADA"),
            "hoy" => query.Where(t => t.cEstado == "PENDIENTE" && t.dFechaVencimiento >= hoy && t.dFechaVencimiento < manana),
            "vencidas" => query.Where(t => t.cEstado == "PENDIENTE" && t.dFechaVencimiento < ahora),
            "completadas" => query.Where(t => t.cEstado == "COMPLETADA"),
            "todas" => query.Where(t => t.cEstado != "CANCELADA"),
            _ => query
        };
        var total = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));

        var items = await query
            .Include(t => t.Cliente)
            .Include(t => t.AsignadoA)
            .OrderBy(t => t.dFechaVencimiento)
            .ThenBy(t => t.nTarea)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                id = t.nTarea,
                titulo = t.cTitulo,
                descripcion = t.cDescripcion,
                vence = t.dFechaVencimiento,
                estado = t.cEstado == "PENDIENTE" && t.dFechaVencimiento < ahora ? "VENCIDA" : t.cEstado,
                conversacionId = t.nConversacion,
                oportunidadId = t.nOportunidad,
                cliente = t.Cliente == null ? null : new { id = t.Cliente.nCliente, nombre = t.Cliente.cNombre },
                asignadoA = t.AsignadoA == null ? null : t.AsignadoA.cNombre,
                asignadoAId = t.nAsignadoA
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items, resumen });
    }

    [HttpGet("alertas")]
    public async Task<IActionResult> Alertas(
        [FromQuery] int dias = 3,
        [FromQuery] int? usuarioId = null)
    {
        var ahora = DateTime.Now;
        var fechaLimite = ahora.AddDays(dias);

        var query = _context.Tareas
            .AsNoTracking()
            .Where(tarea => tarea.cEstado == "PENDIENTE" && tarea.dFechaVencimiento <= fechaLimite)
            .AsQueryable();

        if (usuarioId.HasValue && _access.TieneAccesoGlobal)
        {
            query = query.Where(tarea => tarea.nAsignadoA == usuarioId.Value);
        }
        else if (!_access.TieneAccesoGlobal && _access.UsuarioActualId.HasValue)
        {
            query = query.Where(tarea => tarea.nAsignadoA == _access.UsuarioActualId.Value || tarea.nCreadoPor == _access.UsuarioActualId.Value);
        }

        var items = await query
            .Include(tarea => tarea.Cliente)
            .Include(tarea => tarea.AsignadoA)
            .OrderBy(tarea => tarea.dFechaVencimiento)
            .Select(tarea => new
            {
                id = tarea.nTarea,
                titulo = tarea.cTitulo,
                descripcion = tarea.cDescripcion,
                vence = tarea.dFechaVencimiento,
                vencida = tarea.dFechaVencimiento < ahora,
                cliente = tarea.Cliente == null ? null : new { id = tarea.Cliente.nCliente, nombre = tarea.Cliente.cNombre },
                asignadoA = tarea.AsignadoA == null ? null : tarea.AsignadoA.cNombre,
                asignadoAId = tarea.nAsignadoA,
                prioridad = tarea.dFechaVencimiento < ahora ? "URGENTE" : tarea.dFechaVencimiento <= ahora.AddDays(1) ? "ALTA" : "MEDIA"
            })
            .ToListAsync();

        return Ok(new
        {
            ahora,
            dias,
            total = items.Count,
            urgentes = items.Count(item => item.vencida),
            items
        });
    }

    [HttpPost("revisar-vencidas")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> RevisarVencidas()
    {
        var ahora = DateTime.Now;
        var tareasVencidas = await _context.Tareas
            .Where(tarea => tarea.cEstado == "PENDIENTE" && tarea.dFechaVencimiento < ahora)
            .ToListAsync();

        foreach (var tarea in tareasVencidas)
        {
            tarea.cEstado = "VENCIDA";
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            actualizadas = tareasVencidas.Count,
            items = tareasVencidas.Select(tarea => new { id = tarea.nTarea, titulo = tarea.cTitulo })
        });
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearTareaDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (dto.ClienteId.HasValue && !await _access.PuedeAccederClienteAsync(dto.ClienteId.Value))
        {
            return Forbid();
        }

        if (dto.ConversacionId.HasValue && !await _access.PuedeAccederConversacionAsync(dto.ConversacionId.Value))
        {
            return Forbid();
        }

        if (dto.OportunidadId.HasValue && !await _access.PuedeAccederOportunidadAsync(dto.OportunidadId.Value))
        {
            return Forbid();
        }

        long? clienteRelacionadoId = dto.ClienteId;

        if (dto.ConversacionId.HasValue)
        {
            var conversacionClienteId = await _context.Conversaciones
                .AsNoTracking()
                .Where(conversacion => conversacion.nConversacion == dto.ConversacionId.Value)
                .Select(conversacion => (long?)conversacion.nCliente)
                .FirstOrDefaultAsync();

            if (!clienteRelacionadoId.HasValue)
            {
                clienteRelacionadoId = conversacionClienteId;
            }
            else if (clienteRelacionadoId != conversacionClienteId)
            {
                return BadRequest("La conversación no pertenece al cliente indicado.");
            }
        }

        if (dto.OportunidadId.HasValue)
        {
            var oportunidadClienteId = await _context.Oportunidades
                .AsNoTracking()
                .Where(oportunidad => oportunidad.nOportunidad == dto.OportunidadId.Value)
                .Select(oportunidad => (long?)oportunidad.nCliente)
                .FirstOrDefaultAsync();

            if (!clienteRelacionadoId.HasValue)
            {
                clienteRelacionadoId = oportunidadClienteId;
            }
            else if (clienteRelacionadoId != oportunidadClienteId)
            {
                return BadRequest("La oportunidad no pertenece al cliente indicado.");
            }
        }

        var tarea = new Tarea
        {
            nCliente = clienteRelacionadoId,
            nConversacion = dto.ConversacionId,
            nOportunidad = dto.OportunidadId,
            cTitulo = dto.Titulo!.Trim(),
            cDescripcion = dto.Descripcion?.Trim(),
            dFechaVencimiento = dto.FechaVencimiento,
            nAsignadoA = _access.EsAsesor ? UsuarioActualId : dto.AsignadoAId,
            nCreadoPor = UsuarioActualId,
            cEstado = "PENDIENTE",
            dFechaCreacion = DateTime.Now
        };

        _context.Tareas.Add(tarea);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id = tarea.nTarea });
    }

    [HttpPut("{id:long}/completar")]
    public async Task<IActionResult> Completar(long id)
    {
        var tarea = await _context.Tareas.FindAsync(id);
        if (tarea == null) return NotFound("Tarea no encontrada.");

        if (!await _access.PuedeAccederTareaAsync(id))
        {
            return Forbid();
        }

        tarea.cEstado = "COMPLETADA";
        tarea.dFechaCompletada = DateTime.Now;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Cancelar(long id)
    {
        var tarea = await _context.Tareas.FindAsync(id);
        if (tarea == null) return NotFound("Tarea no encontrada.");

        if (!await _access.PuedeAccederTareaAsync(id))
        {
            return Forbid();
        }

        tarea.cEstado = "CANCELADA";
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id });
    }
}

public sealed class CrearTareaDto
{
    public long? ClienteId { get; set; }
    public long? ConversacionId { get; set; }
    public long? OportunidadId { get; set; }

    [Required(ErrorMessage = "El título es obligatorio.")]
    [StringLength(200)]
    public string? Titulo { get; set; }

    [StringLength(2000)]
    public string? Descripcion { get; set; }

    [Required(ErrorMessage = "La fecha de vencimiento es obligatoria.")]
    public DateTime FechaVencimiento { get; set; }

    public int? AsignadoAId { get; set; }
}
