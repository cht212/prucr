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
[Route("api/exportaciones")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class ExportacionesController : ControllerBase
{
    private readonly CrmDbContext _context;
    private readonly CrmAccessService _access;

    public ExportacionesController(CrmDbContext context, CrmAccessService access)
    {
        _context = context;
        _access = access;
    }

    private int? UsuarioActualId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var items = await _context.ReportesExportacion
            .AsNoTracking()
            .Include(r => r.CreadoPor)
            .OrderByDescending(r => r.dFechaCreacion)
            .Select(r => new
            {
                id = r.nReporte,
                nombre = r.cNombre,
                tipo = r.cTipo,
                entidad = r.cEntidad,
                ruta = r.cRuta,
                fecha = r.dFechaCreacion,
                creadoPor = r.CreadoPor == null ? null : r.CreadoPor.cNombre
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] CrearExportacionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nombre))
        {
            return BadRequest("El nombre es obligatorio.");
        }

        var registro = new ReporteExportacion
        {
            cNombre = dto.Nombre.Trim(),
            cTipo = dto.Tipo ?? "CSV",
            cEntidad = dto.Entidad ?? "GENERAL",
            cRuta = dto.Ruta ?? "/downloads/reportes",
            nCreadoPor = UsuarioActualId,
            dFechaCreacion = DateTime.Now
        };

        _context.ReportesExportacion.Add(registro);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, id = registro.nReporte });
    }
}

public sealed class CrearExportacionDto
{
    public string Nombre { get; set; } = string.Empty;
    public string? Tipo { get; set; }
    public string? Entidad { get; set; }
    public string? Ruta { get; set; }
}
