using System.ComponentModel.DataAnnotations;
using CRM.Data.Data;
using CRM.Data.Models;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/etiquetas")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class EtiquetasController : ControllerBase
{
    private readonly CrmDbContext _context;
    private readonly CrmAccessService _access;

    public EtiquetasController(CrmDbContext context, CrmAccessService access)
    {
        _context = context;
        _access = access;
    }

    [HttpGet]
    [CrmPermission(CrmPermissionService.ViewCustomerDetails)]
    public async Task<IActionResult> Listar()
    {
        var etiquetas = await _context.Etiquetas
            .AsNoTracking()
            .OrderBy(e => e.cNombre)
            .Select(e => new { id = e.nEtiqueta, nombre = e.cNombre, color = e.cColor })
            .ToListAsync();

        return Ok(etiquetas);
    }

    [HttpPost]
    [Authorize(Roles = "Administrador,Supervisor")]
    public async Task<IActionResult> Crear([FromBody] CrearEtiquetaDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var nombre = dto.Nombre!.Trim();
        if (await _context.Etiquetas.AnyAsync(e => e.cNombre == nombre))
        {
            return Conflict("Ya existe una etiqueta con ese nombre.");
        }

        var etiqueta = new Etiqueta
        {
            cNombre = nombre,
            cColor = string.IsNullOrWhiteSpace(dto.Color) ? "#6366F1" : dto.Color.Trim()
        };

        _context.Etiquetas.Add(etiqueta);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id = etiqueta.nEtiqueta });
    }

    [HttpPost("clientes/{clienteId:long}/{etiquetaId:int}")]
    public async Task<IActionResult> AsignarACliente(long clienteId, int etiquetaId)
    {
        var clienteExiste = await _context.Clientes.AnyAsync(c => c.nCliente == clienteId);
        var etiquetaExiste = await _context.Etiquetas.AnyAsync(e => e.nEtiqueta == etiquetaId);
        if (!clienteExiste || !etiquetaExiste) return NotFound("Cliente o etiqueta no encontrada.");

        if (!await _access.PuedeAccederClienteAsync(clienteId))
        {
            return Forbid();
        }

        var yaAsignada = await _context.ClienteEtiquetas
            .AnyAsync(ce => ce.nCliente == clienteId && ce.nEtiqueta == etiquetaId);
        if (yaAsignada) return Ok(new { success = true });

        _context.ClienteEtiquetas.Add(new ClienteEtiqueta
        {
            nCliente = clienteId,
            nEtiqueta = etiquetaId,
            dFechaAsignacion = DateTime.Now
        });
        await _context.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpDelete("clientes/{clienteId:long}/{etiquetaId:int}")]
    public async Task<IActionResult> QuitarDeCliente(long clienteId, int etiquetaId)
    {
        var relacion = await _context.ClienteEtiquetas
            .FirstOrDefaultAsync(ce => ce.nCliente == clienteId && ce.nEtiqueta == etiquetaId);
        if (relacion == null) return NotFound();

        if (!await _access.PuedeAccederClienteAsync(clienteId))
        {
            return Forbid();
        }

        _context.ClienteEtiquetas.Remove(relacion);
        await _context.SaveChangesAsync();

        return Ok(new { success = true });
    }

    [HttpGet("clientes/{clienteId:long}")]
    [CrmPermission(CrmPermissionService.ViewCustomerDetails)]
    public async Task<IActionResult> DeCliente(long clienteId)
    {
        if (!await _access.PuedeAccederClienteAsync(clienteId))
        {
            return Forbid();
        }

        var etiquetas = await _context.ClienteEtiquetas
            .AsNoTracking()
            .Where(ce => ce.nCliente == clienteId)
            .Include(ce => ce.Etiqueta)
            .Select(ce => new { id = ce.Etiqueta.nEtiqueta, nombre = ce.Etiqueta.cNombre, color = ce.Etiqueta.cColor })
            .ToListAsync();

        return Ok(etiquetas);
    }
}

public sealed class CrearEtiquetaDto
{
    [Required(ErrorMessage = "El nombre de la etiqueta es obligatorio.")]
    [StringLength(60)]
    public string? Nombre { get; set; }

    [StringLength(20)]
    public string? Color { get; set; }
}
