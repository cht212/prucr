using System.ComponentModel.DataAnnotations;
using System.Text;
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
// OPORTUNIDADES (embudo de ventas)
// =========================================================
//
// Antes, el "pipeline" del CRM solo reflejaba el estado de
// atención de la conversación (NUEVO/ABIERTO/CERRADO/PERDIDO),
// sin monto, etapa comercial ni probabilidad de cierre. Este
// controlador agrega el embudo de ventas real.
//
// =========================================================

[ApiController]
[Route("api/oportunidades")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class OportunidadesController : ControllerBase
{
    private static readonly string[] EtapasValidas =
        { "NUEVA", "CALIFICADA", "PROPUESTA", "NEGOCIACION", "GANADA", "PERDIDA" };

    private readonly CrmDbContext _context;
    private readonly AuditoriaService _auditoria;
    private readonly CrmAccessService _access;

    public OportunidadesController(CrmDbContext context, AuditoriaService auditoria, CrmAccessService access)
    {
        _context = context;
        _auditoria = auditoria;
        _access = access;
    }

    private int? UsuarioActualId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? etapa = null,
        [FromQuery] long? clienteId = null,
        [FromQuery] long? conversacionId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = _access.FiltrarOportunidades(_context.Oportunidades.AsNoTracking());
        if (clienteId.HasValue) query = query.Where(o => o.nCliente == clienteId.Value);
        if (conversacionId.HasValue) query = query.Where(o => o.nConversacion == conversacionId.Value);
        var limiteSemana = DateTime.Today.AddDays(8);
        var porEtapa = await query.GroupBy(o => o.cEtapa).Select(g => new
        {
            etapa = g.Key, cantidad = g.Count(), monto = g.Sum(o => o.nMonto),
            cierreSemana = g.Count(o => o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA" && o.dFechaCierreEstimada <= limiteSemana)
        }).ToListAsync();
        var abiertas = porEtapa.Where(e => e.etapa != "GANADA" && e.etapa != "PERDIDA").ToList();
        var ganadas = porEtapa.Where(e => e.etapa == "GANADA").ToList();
        var perdidas = porEtapa.Where(e => e.etapa == "PERDIDA").Sum(e => e.cantidad);
        var cantidadGanadas = ganadas.Sum(e => e.cantidad);
        var resumen = new
        {
            abiertas = abiertas.Sum(e => e.cantidad), ganadas = cantidadGanadas, perdidas,
            montoAbierto = abiertas.Sum(e => e.monto), montoGanado = ganadas.Sum(e => e.monto),
            cierreSemana = abiertas.Sum(e => e.cierreSemana),
            winRate = cantidadGanadas + perdidas == 0 ? 0m : Math.Round(cantidadGanadas * 100m / (cantidadGanadas + perdidas), 1)
        };
        if (!string.IsNullOrWhiteSpace(etapa))
        {
            query = query.Where(o => o.cEtapa == etapa.Trim().ToUpperInvariant());
        }

        var total = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));

        var items = await query
            .Include(o => o.Cliente)
            .Include(o => o.UsuarioAsignado)
            .OrderByDescending(o => o.dFechaCreacion)
            .ThenByDescending(o => o.nOportunidad)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                id = o.nOportunidad,
                titulo = o.cTitulo,
                monto = o.nMonto,
                moneda = o.cMoneda,
                etapa = o.cEtapa,
                probabilidad = o.nProbabilidad,
                fechaCierreEstimada = o.dFechaCierreEstimada,
                fechaCierreReal = o.dFechaCierreReal,
                motivoPerdida = o.cMotivoPerdida,
                conversacionId = o.nConversacion,
                cliente = new { id = o.Cliente.nCliente, nombre = o.Cliente.cNombre },
                asesor = o.UsuarioAsignado == null ? null : o.UsuarioAsignado.cNombre,
                asesorId = o.nUsuarioAsignado
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items, resumen, porEtapa });
    }

    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar([FromQuery] string? etapa = null)
    {
        var query = _access.FiltrarOportunidades(_context.Oportunidades.AsNoTracking());
        if (!string.IsNullOrWhiteSpace(etapa) && !etapa.Equals("TODAS", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(o => o.cEtapa == etapa.Trim().ToUpperInvariant());
        }

        var filas = await query
            .Include(o => o.Cliente)
            .Include(o => o.UsuarioAsignado)
            .OrderByDescending(o => o.dFechaCreacion)
            .Select(o => new string?[]
            {
                o.cTitulo,
                o.Cliente.cNombre,
                o.nMonto.ToString(),
                o.cMoneda,
                o.cEtapa,
                o.nProbabilidad.ToString(),
                o.UsuarioAsignado == null ? null : o.UsuarioAsignado.cNombre,
                o.dFechaCreacion.ToString(),
                o.dFechaCierreEstimada == null ? null : o.dFechaCierreEstimada.Value.ToString(),
                o.dFechaCierreReal == null ? null : o.dFechaCierreReal.Value.ToString(),
                o.cMotivoPerdida
            })
            .ToListAsync();

        return CsvFile("oportunidades-crm.csv", new[] { "Titulo", "Cliente", "Monto", "Moneda", "Etapa", "Probabilidad", "Asesor", "Creacion", "Cierre estimado", "Cierre real", "Motivo perdida" }, filas);
    }
    // Vista resumida del embudo: monto total y cantidad por etapa
    [HttpGet("resumen")]
    public async Task<IActionResult> Resumen()
    {
        var resumen = await _access.FiltrarOportunidades(_context.Oportunidades.AsNoTracking())
            .GroupBy(o => o.cEtapa)
            .Select(g => new
            {
                etapa = g.Key,
                cantidad = g.Count(),
                montoTotal = g.Sum(o => o.nMonto)
            })
            .ToListAsync();

        return Ok(resumen);
    }

    [HttpGet("pipeline")]
    public async Task<IActionResult> PipelineResumen()
    {
        var oportunidades = await _access.FiltrarOportunidades(
                _context.Oportunidades
                    .AsNoTracking()
                    .Include(o => o.UsuarioAsignado)
                    .Include(o => o.Cliente))
            .ToListAsync();

        var porEtapa = oportunidades
            .GroupBy(o => o.cEtapa)
            .Select(grupo => new
            {
                etapa = grupo.Key,
                cantidad = grupo.Count(),
                montoTotal = grupo.Sum(o => o.nMonto),
                promedio = grupo.Count() == 0 ? 0m : grupo.Average(o => o.nMonto),
                probabilidadPromedio = grupo.Count() == 0 ? 0 : (int)Math.Round(grupo.Average(o => o.nProbabilidad))
            })
            .OrderBy(item => new[] { "NUEVA", "CALIFICADA", "PROPUESTA", "NEGOCIACION", "GANADA", "PERDIDA" }
                .ToList().IndexOf(item.etapa))
            .ToList();

        var porAsesor = oportunidades
            .Where(o => o.nUsuarioAsignado.HasValue)
            .GroupBy(o => o.nUsuarioAsignado!.Value)
            .Select(grupo => new
            {
                asesorId = grupo.Key,
                asesor = grupo.First().UsuarioAsignado?.cNombre ?? "Sin nombre",
                cantidad = grupo.Count(),
                montoTotal = grupo.Sum(o => o.nMonto),
                ganadas = grupo.Count(o => o.cEtapa == "GANADA"),
                perdidas = grupo.Count(o => o.cEtapa == "PERDIDA"),
                abiertas = grupo.Count(o => o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA")
            })
            .OrderByDescending(item => item.montoTotal)
            .ToList();

        var resumenGeneral = new
        {
            total = oportunidades.Count,
            montoTotal = oportunidades.Sum(o => o.nMonto),
            ganado = oportunidades.Where(o => o.cEtapa == "GANADA").Sum(o => o.nMonto),
            perdido = oportunidades.Where(o => o.cEtapa == "PERDIDA").Sum(o => o.nMonto),
            abiertas = oportunidades.Count(o => o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA")
        };

        return Ok(new
        {
            resumen = resumenGeneral,
            porEtapa,
            porAsesor
        });
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] CrearOportunidadDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        if (dto.Monto <= 0)
        {
            return BadRequest("El monto debe ser mayor a cero.");
        }

        if (!dto.FechaCierreEstimada.HasValue)
        {
            return BadRequest("La fecha estimada de cierre es obligatoria.");
        }

        if (dto.FechaCierreEstimada.Value.Date < DateTime.Today)
        {
            return BadRequest("La fecha estimada de cierre no puede estar en el pasado.");
        }

        var clienteExiste = await _context.Clientes.AnyAsync(c => c.nCliente == dto.ClienteId);
        if (!clienteExiste) return BadRequest("El cliente no existe.");

        if (!await _access.PuedeAccederClienteAsync(dto.ClienteId))
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

            if (conversacionClienteId != dto.ClienteId)
            {
                return BadRequest("La conversación no pertenece al cliente indicado.");
            }
        }

        var oportunidad = new Oportunidad
        {
            nCliente = dto.ClienteId,
            nConversacion = dto.ConversacionId,
            nUsuarioAsignado = _access.EsAsesor ? UsuarioActualId : dto.UsuarioAsignadoId,
            cTitulo = dto.Titulo!.Trim(),
            nMonto = dto.Monto,
            cMoneda = string.IsNullOrWhiteSpace(dto.Moneda) ? "PEN" : dto.Moneda.Trim().ToUpperInvariant(),
            cEtapa = "NUEVA",
            nProbabilidad = dto.Probabilidad ?? 10,
            dFechaCierreEstimada = dto.FechaCierreEstimada,
            nCreadoPor = UsuarioActualId,
            dFechaCreacion = DateTime.Now
        };

        _context.Oportunidades.Add(oportunidad);
        await _context.SaveChangesAsync();

        await _auditoria.RegistrarAsync("Oportunidad", oportunidad.nOportunidad, "CREACION", null,
            oportunidad.cTitulo, UsuarioActualId);

        return Ok(new { success = true, id = oportunidad.nOportunidad });
    }

    [HttpPut("{id:long}/etapa")]
    public async Task<IActionResult> CambiarEtapa(long id, [FromBody] CambiarEtapaDto dto)
    {
        var etapa = (dto.Etapa ?? string.Empty).Trim().ToUpperInvariant();
        if (!EtapasValidas.Contains(etapa)) return BadRequest("Etapa no válida.");

        var oportunidad = await _context.Oportunidades.FindAsync(id);
        if (oportunidad == null) return NotFound("Oportunidad no encontrada.");

        if (!await _access.PuedeAccederOportunidadAsync(id))
        {
            return Forbid();
        }

        if (etapa == "PERDIDA" && string.IsNullOrWhiteSpace(dto.MotivoPerdida))
        {
            return BadRequest("Debes indicar el motivo de la pérdida.");
        }

        var etapaAnterior = oportunidad.cEtapa;
        oportunidad.cEtapa = etapa;
        oportunidad.dFechaActualizacion = DateTime.Now;

        if (etapa is "GANADA" or "PERDIDA")
        {
            oportunidad.dFechaCierreReal = DateTime.Now;
            oportunidad.nProbabilidad = etapa == "GANADA" ? 100 : 0;
            oportunidad.cMotivoPerdida = etapa == "PERDIDA" ? dto.MotivoPerdida!.Trim() : null;
        }

        await _context.SaveChangesAsync();
        var nuevoValorAuditoria = etapa == "PERDIDA"
            ? $"{etapa}: {oportunidad.cMotivoPerdida}"
            : etapa;
        await _auditoria.RegistrarAsync("Oportunidad", id, "CAMBIO_ETAPA", etapaAnterior, nuevoValorAuditoria, UsuarioActualId);

        return Ok(new { success = true, id, etapa });
    }

    [HttpPut("{id:long}/asignar")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> Asignar(long id, [FromBody] AsignacionDto dto)
    {
        var oportunidad = await _context.Oportunidades.FindAsync(id);
        if (oportunidad == null) return NotFound("Oportunidad no encontrada.");

        var anterior = oportunidad.nUsuarioAsignado?.ToString() ?? "sin asignar";
        oportunidad.nUsuarioAsignado = dto.UsuarioId;
        oportunidad.dFechaActualizacion = DateTime.Now;
        await _context.SaveChangesAsync();

        await _auditoria.RegistrarAsync("Oportunidad", id, "REASIGNACION", anterior,
            dto.UsuarioId?.ToString() ?? "sin asignar", UsuarioActualId);

        return Ok(new { success = true, id, usuarioId = dto.UsuarioId });
    }

    private static IActionResult CsvFile(string fileName, string[] headers, IEnumerable<string?[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return new FileContentResult(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv; charset=utf-8")
        {
            FileDownloadName = fileName
        };
    }

    private static string EscapeCsv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}

public sealed class CrearOportunidadDto
{
    [Required] public long ClienteId { get; set; }
    public long? ConversacionId { get; set; }
    public int? UsuarioAsignadoId { get; set; }

    [Required(ErrorMessage = "El título es obligatorio.")]
    [StringLength(200)]
    public string? Titulo { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "El monto debe ser mayor a cero.")]
    public decimal Monto { get; set; }

    public string? Moneda { get; set; }

    [Range(0, 100)]
    public int? Probabilidad { get; set; }

    [Required(ErrorMessage = "La fecha estimada de cierre es obligatoria.")]
    public DateTime? FechaCierreEstimada { get; set; }
}

public sealed class CambiarEtapaDto
{
    public string? Etapa { get; set; }
    public string? MotivoPerdida { get; set; }
}
