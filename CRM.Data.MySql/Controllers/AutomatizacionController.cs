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
[Route("api/automatizacion")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class AutomatizacionController : ControllerBase
{
    private readonly CrmDbContext _context;
    private readonly CrmAccessService _access;

    public AutomatizacionController(CrmDbContext context, CrmAccessService access)
    {
        _context = context;
        _access = access;
    }

    private int? UsuarioActualId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet("reglas")]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> ListarReglas()
    {
        var reglas = await _context.ReglasAutomaticas
            .AsNoTracking()
            .Include(r => r.AsignadoA)
            .OrderByDescending(r => r.dFechaCreacion)
            .Select(r => new
            {
                id = r.nRegla,
                nombre = r.cNombre,
                entidad = r.cEntidad,
                evento = r.cEvento,
                condicion = r.cCondicion,
                accion = r.cAccion,
                valorAccion = r.cValorAccion,
                asignadoA = r.AsignadoA == null ? null : new { id = r.AsignadoA.nUsuario, nombre = r.AsignadoA.cNombre },
                activa = r.bActiva
            })
            .ToListAsync();

        return Ok(reglas);
    }

    [HttpPost("reglas")]
    public async Task<IActionResult> CrearRegla([FromBody] CrearReglaDto dto)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        if (string.IsNullOrWhiteSpace(dto.Nombre))
        {
            return BadRequest("El nombre es obligatorio.");
        }

        var regla = new ReglaAutomatica
        {
            cNombre = dto.Nombre.Trim(),
            cEntidad = dto.Entidad ?? "Oportunidad",
            cEvento = dto.Evento ?? "CAMBIO_ETAPA",
            cCondicion = dto.Condicion ?? "ETAPA=PROPUESTA",
            cAccion = dto.Accion ?? "CREAR_TAREA",
            cValorAccion = dto.ValorAccion,
            nAsignadoA = dto.AsignadoAId,
            nCreadoPor = UsuarioActualId,
            bActiva = dto.Activa,
            dFechaCreacion = DateTime.Now
        };

        _context.ReglasAutomaticas.Add(regla);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id = regla.nRegla });
    }

    [HttpPost("ejecutar/{reglaId:long}")]
    public async Task<IActionResult> EjecutarRegla(long reglaId)
    {
        var regla = await _context.ReglasAutomaticas
            .FirstOrDefaultAsync(r => r.nRegla == reglaId && r.bActiva);

        if (regla == null)
        {
            return NotFound("Regla no encontrada o inactiva.");
        }

        if (regla.cAccion == "CREAR_TAREA")
        {
            var tarea = new Tarea
            {
                cTitulo = string.IsNullOrWhiteSpace(regla.cValorAccion) ? "Seguimiento automático" : regla.cValorAccion,
                cDescripcion = $"Generada por regla automática: {regla.cNombre}",
                dFechaVencimiento = DateTime.Now.AddDays(2),
                cEstado = "PENDIENTE",
                nAsignadoA = regla.nAsignadoA ?? UsuarioActualId,
                nCreadoPor = UsuarioActualId,
                dFechaCreacion = DateTime.Now
            };

            _context.Tareas.Add(tarea);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, tareaId = tarea.nTarea, reglaId = regla.nRegla });
        }

        return Ok(new { success = true, reglaId = regla.nRegla, accion = regla.cAccion, mensaje = "Regla ejecutada sin acción adicional definida." });
    }
}

public sealed class CrearReglaDto
{
    [Required]
    [StringLength(200)]
    public string Nombre { get; set; } = string.Empty;

    public string? Entidad { get; set; }
    public string? Evento { get; set; }
    public string? Condicion { get; set; }
    public string? Accion { get; set; }
    public string? ValorAccion { get; set; }
    public int? AsignadoAId { get; set; }
    public bool Activa { get; set; } = true;
}
