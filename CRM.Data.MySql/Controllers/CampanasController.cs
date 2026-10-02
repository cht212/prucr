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
[Route("api/campanas")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class CampanasController : ControllerBase
{
    private readonly CrmDbContext _context;
    private readonly CrmAccessService _access;

    public CampanasController(CrmDbContext context, CrmAccessService access)
    {
        _context = context;
        _access = access;
    }

    private int? UsuarioActualId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? estado = null,
        [FromQuery] int? usuarioId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = _context.Campanas.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(estado))
        {
            query = query.Where(c => c.cEstado == estado.Trim().ToUpperInvariant());
        }

        if (usuarioId.HasValue && _access.TieneAccesoGlobal)
        {
            query = query.Where(c => c.nAsignadoA == usuarioId.Value);
        }
        else if (!_access.TieneAccesoGlobal && UsuarioActualId.HasValue)
        {
            query = query.Where(c => c.nAsignadoA == UsuarioActualId.Value || c.nCreadoPor == UsuarioActualId.Value);
        }

        var total = await query.CountAsync();

        var items = await query
            .Include(c => c.AsignadoA)
            .OrderByDescending(c => c.dFechaCreacion)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                id = c.nCampana,
                nombre = c.cNombre,
                descripcion = c.cDescripcion,
                tipo = c.cTipo,
                estado = c.cEstado,
                fechaInicio = c.dFechaInicio,
                fechaFin = c.dFechaFin,
                asignadoA = c.AsignadoA == null ? null : new { id = c.AsignadoA.nUsuario, nombre = c.AsignadoA.cNombre },
                clientesAsignados = _context.CampanasClientes.Count(cc => cc.nCampana == c.nCampana),
                respondieron = _context.CampanasClientes.Count(cc => cc.nCampana == c.nCampana && cc.cEstado == "RESPONDIO")
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearCampanaDto dto)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var campana = new Campana
        {
            cNombre = dto.Nombre.Trim(),
            cDescripcion = dto.Descripcion?.Trim(),
            cTipo = string.IsNullOrWhiteSpace(dto.Tipo) ? "WHATSAPP" : dto.Tipo.Trim().ToUpperInvariant(),
            cEstado = string.IsNullOrWhiteSpace(dto.Estado) ? "ACTIVA" : dto.Estado.Trim().ToUpperInvariant(),
            dFechaInicio = dto.FechaInicio,
            dFechaFin = dto.FechaFin,
            nAsignadoA = _access.EsAsesor ? UsuarioActualId : dto.AsignadoAId,
            nCreadoPor = UsuarioActualId,
            dFechaCreacion = DateTime.Now
        };

        _context.Campanas.Add(campana);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id = campana.nCampana });
    }

    [HttpPost("{id:long}/clientes")]
    public async Task<IActionResult> AsignarClientes(long id, [FromBody] List<long> clienteIds)
    {
        var campana = await _context.Campanas.FindAsync(id);
        if (campana == null)
        {
            return NotFound("Campaña no encontrada.");
        }

        if (!await _access.PuedeAccederModuloAsync("inbox"))
        {
            return Forbid();
        }

        var ids = clienteIds.Distinct().ToList();
        foreach (var clienteId in ids)
        {
            var clienteExiste = await _context.Clientes.AnyAsync(c => c.nCliente == clienteId);
            if (!clienteExiste)
            {
                continue;
            }

            var yaExiste = await _context.CampanasClientes.AnyAsync(cc => cc.nCampana == id && cc.nCliente == clienteId);
            if (!yaExiste)
            {
                _context.CampanasClientes.Add(new CampanaCliente
                {
                    nCampana = id,
                    nCliente = clienteId,
                    cEstado = "PENDIENTE",
                    dFechaAsignacion = DateTime.Now
                });
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { success = true, asignados = ids.Count });
    }

    [HttpPut("{id:long}/clientes/{clienteId:long}/estado")]
    public async Task<IActionResult> ActualizarEstadoCliente(long id, long clienteId, [FromBody] EstadoCampanaDto dto)
    {
        var asignacion = await _context.CampanasClientes.FirstOrDefaultAsync(cc => cc.nCampana == id && cc.nCliente == clienteId);
        if (asignacion == null)
        {
            return NotFound("El cliente no está asignado a la campaña.");
        }

        var estado = (dto.Estado ?? string.Empty).Trim().ToUpperInvariant();
        var validos = new[] { "PENDIENTE", "CONTACTADO", "RESPONDIO", "NO_RESPONDIO" };
        if (!validos.Contains(estado))
        {
            return BadRequest("Estado no válido.");
        }

        asignacion.cEstado = estado;
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id, clienteId, estado });
    }

    [HttpGet("{id:long}/clientes")]
    [CrmPermission(CrmPermissionService.ModuleMarketing)]
    public async Task<IActionResult> ClientesDeCampana(long id)
    {
        var items = await _context.CampanasClientes
            .AsNoTracking()
            .Where(cc => cc.nCampana == id)
            .Include(cc => cc.Cliente)
            .Select(cc => new
            {
                clienteId = cc.nCliente,
                nombre = cc.Cliente.cNombre,
                telefono = cc.Cliente.cTelefono,
                estado = cc.cEstado,
                fechaAsignacion = cc.dFechaAsignacion
            })
            .ToListAsync();

        return Ok(items);
    }
}

public sealed class CrearCampanaDto
{
    [Required]
    [StringLength(200)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Descripcion { get; set; }

    public string? Tipo { get; set; }
    public string? Estado { get; set; }
    public DateTime FechaInicio { get; set; } = DateTime.Now;
    public DateTime? FechaFin { get; set; }
    public int? AsignadoAId { get; set; }
}

public sealed class EstadoCampanaDto
{
    public string? Estado { get; set; }
}
