using CRM.Data.Data;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class DashboardController : ControllerBase
{
    private readonly CrmDbContext _context;
    private readonly CrmAccessService _access;

    public DashboardController(CrmDbContext context, CrmAccessService access)
    {
        _context = context;
        _access = access;
    }

    [HttpGet("ejecutivo")]
    public async Task<IActionResult> Ejecutivo([FromQuery] int? usuarioId = null)
    {
        var usuarioActualId = _access.UsuarioActualId;

        if (!_access.TieneAccesoGlobal)
        {
            if (!usuarioActualId.HasValue)
            {
                return Forbid();
            }

            usuarioId = usuarioActualId.Value;
        }

        if (usuarioId.HasValue)
        {
            if (!_access.TieneAccesoGlobal && usuarioActualId != usuarioId.Value)
            {
                return Forbid();
            }

            var existeUsuario = await _context.Usuarios
                .AsNoTracking()
                .AnyAsync(u => u.nUsuario == usuarioId.Value && u.cEstado == 'A');

            if (!existeUsuario)
            {
                return NotFound("El usuario seleccionado no existe o no está activo.");
            }
        }

        int? filtroUsuario = usuarioId;
        var hoy = DateTime.Now.Date;

        var clientesQuery = _context.Clientes.AsNoTracking().AsQueryable();
        if (filtroUsuario.HasValue)
        {
            clientesQuery = clientesQuery.Where(c => c.Conversaciones.Any(conv => conv.nUsuarioAsignado == filtroUsuario.Value));
        }

        var conversacionesQuery = _context.Conversaciones.AsNoTracking().AsQueryable();
        if (filtroUsuario.HasValue)
        {
            conversacionesQuery = conversacionesQuery.Where(c => c.nUsuarioAsignado == filtroUsuario.Value);
        }

        var tareasQuery = _context.Tareas.AsNoTracking().AsQueryable();
        if (filtroUsuario.HasValue)
        {
            tareasQuery = tareasQuery.Where(t => t.nAsignadoA == filtroUsuario.Value);
        }

        var oportunidadesQuery = _context.Oportunidades.AsNoTracking().AsQueryable();
        if (filtroUsuario.HasValue)
        {
            oportunidadesQuery = oportunidadesQuery.Where(o => o.nUsuarioAsignado == filtroUsuario.Value);
        }

        var clientesNuevos = await clientesQuery.CountAsync(c => c.dFechaRegistro >= hoy);
        var conversacionesAbiertas = await conversacionesQuery.CountAsync(c => c.cEstado != "CERRADO" && c.cEstado != "PERDIDO" && c.cEstado != "NO_RESPONDIO");
        var tareasPendientes = await tareasQuery.CountAsync(t => t.cEstado == "PENDIENTE");
        var tareasVencidas = await tareasQuery.CountAsync(t => t.cEstado == "PENDIENTE" && t.dFechaVencimiento < DateTime.Now);
        var oportunidadesAbiertas = await oportunidadesQuery.CountAsync(o => o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA");
        var oportunidadesGanadas = await oportunidadesQuery.CountAsync(o => o.cEtapa == "GANADA");
        var montoTotalPendiente = await oportunidadesQuery.Where(o => o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA").SumAsync(o => (decimal?)o.nMonto) ?? 0m;
        var montoTotalGanado = await oportunidadesQuery.Where(o => o.cEtapa == "GANADA").SumAsync(o => (decimal?)o.nMonto) ?? 0m;

        var asesoresQuery = _context.Usuarios
            .AsNoTracking()
            .Where(u => u.cEstado == 'A');

        if (filtroUsuario.HasValue)
        {
            asesoresQuery = asesoresQuery.Where(u => u.nUsuario == filtroUsuario.Value);
        }

        var porAsesor = await asesoresQuery
            .Select(u => new
            {
                usuarioId = u.nUsuario,
                nombre = u.cNombre,
                rol = u.cRol,
                conversaciones = _context.Conversaciones.Count(c => c.nUsuarioAsignado == u.nUsuario && c.cEstado != "CERRADO" && c.cEstado != "PERDIDO" && c.cEstado != "NO_RESPONDIO"),
                tareasPendientes = _context.Tareas.Count(t => t.nAsignadoA == u.nUsuario && t.cEstado == "PENDIENTE"),
                oportunidadesAbiertas = _context.Oportunidades.Count(o => o.nUsuarioAsignado == u.nUsuario && o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA"),
                montoEnPipeline = _context.Oportunidades.Where(o => o.nUsuarioAsignado == u.nUsuario && o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA").Sum(o => (decimal?)o.nMonto) ?? 0m
            })
            .OrderByDescending(x => x.montoEnPipeline)
            .ToListAsync();

        var resumen = new
        {
            filtroUsuario,
            clientesNuevos,
            conversacionesAbiertas,
            tareasPendientes,
            tareasVencidas,
            oportunidadesAbiertas,
            oportunidadesGanadas,
            montoTotalPendiente,
            montoTotalGanado,
            porAsesor
        };

        return Ok(resumen);
    }

    [HttpGet("carga-asesores")]
    public async Task<IActionResult> CargaAsesores([FromQuery] int? usuarioId = null)
    {
        if (!_access.TieneAccesoGlobal)
        {
            if (!_access.UsuarioActualId.HasValue)
            {
                return Forbid();
            }

            usuarioId = _access.UsuarioActualId.Value;
        }

        var asesoresQuery = _context.Usuarios
            .AsNoTracking()
            .Where(u => u.cEstado == 'A' && (u.cRol == "Asesor" || u.cRol == "Supervisor"));

        if (usuarioId.HasValue)
        {
            asesoresQuery = asesoresQuery.Where(u => u.nUsuario == usuarioId.Value);
        }

        var asesores = await asesoresQuery
            .OrderBy(u => u.cNombre)
            .Select(u => new
            {
                usuarioId = u.nUsuario,
                nombre = u.cNombre,
                rol = u.cRol,
                conversacionesActivas = _context.Conversaciones.Count(c => c.nUsuarioAsignado == u.nUsuario && c.cEstado != "CERRADO" && c.cEstado != "PERDIDO" && c.cEstado != "NO_RESPONDIO"),
                tareasPendientes = _context.Tareas.Count(t => t.nAsignadoA == u.nUsuario && t.cEstado == "PENDIENTE"),
                tareasVencidas = _context.Tareas.Count(t => t.nAsignadoA == u.nUsuario && t.cEstado == "PENDIENTE" && t.dFechaVencimiento < DateTime.Now),
                oportunidadesAbiertas = _context.Oportunidades.Count(o => o.nUsuarioAsignado == u.nUsuario && o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA"),
                montoAbierto = _context.Oportunidades.Where(o => o.nUsuarioAsignado == u.nUsuario && o.cEtapa != "GANADA" && o.cEtapa != "PERDIDA").Sum(o => (decimal?)o.nMonto) ?? 0m,
                clientesAsignados = _context.Clientes.Count(c => c.Conversaciones.Any(conv => conv.nUsuarioAsignado == u.nUsuario))
            })
            .ToListAsync();

        var items = asesores
            .Select(item => new
            {
                item.usuarioId,
                item.nombre,
                item.rol,
                item.conversacionesActivas,
                item.tareasPendientes,
                item.tareasVencidas,
                item.oportunidadesAbiertas,
                item.montoAbierto,
                item.clientesAsignados,
                cargaTotal = item.conversacionesActivas + item.tareasPendientes + item.oportunidadesAbiertas
            })
            .OrderByDescending(item => item.cargaTotal)
            .ThenBy(item => item.nombre)
            .ToList();

        return Ok(new
        {
            total = items.Count,
            usuarioId = usuarioId,
            items
        });
    }

    [HttpGet("hoy")]
    public async Task<IActionResult> Hoy([FromQuery] int? usuarioId = null)
    {
        var usuarioActualId = _access.UsuarioActualId;
        if (!_access.TieneAccesoGlobal)
        {
            if (!usuarioActualId.HasValue)
            {
                return Forbid();
            }

            usuarioId = usuarioActualId.Value;
        }

        if (usuarioId.HasValue && !await _context.Usuarios.AsNoTracking()
                .AnyAsync(usuario => usuario.nUsuario == usuarioId.Value && usuario.cEstado == 'A'))
        {
            return NotFound("El usuario seleccionado no existe o no está activo.");
        }

        var ahora = DateTime.Now;
        var inicioHoy = ahora.Date;
        var inicioManana = inicioHoy.AddDays(1);
        var limiteOportunidades = inicioHoy.AddDays(7);
        var limiteFuturo = inicioManana;

        var clientesQuery = _access.FiltrarClientes(_context.Clientes.AsNoTracking());
        var conversacionesQuery = _access.FiltrarConversaciones(_context.Conversaciones.AsNoTracking());
        var tareasQuery = _access.FiltrarTareas(_context.Tareas.AsNoTracking());
        var oportunidadesQuery = _access.FiltrarOportunidades(_context.Oportunidades.AsNoTracking());

        // Los comentarios públicos pertenecen a Marketing. No deben crear una
        // acción comercial ni hacer que el autor aparezca como cliente nuevo.
        clientesQuery = clientesQuery.Where(cliente =>
            !cliente.Conversaciones.Any(conversacion => conversacion.Mensajes.Any()) ||
            cliente.Conversaciones.Any(conversacion => conversacion.Mensajes.Any(mensaje =>
                mensaje.cTipo != "comment" && mensaje.cTipo != "comment_reply")));

        if (usuarioId.HasValue && _access.TieneAccesoGlobal)
        {
            clientesQuery = clientesQuery.Where(cliente => cliente.Conversaciones.Any(conversacion => conversacion.nUsuarioAsignado == usuarioId.Value));
            conversacionesQuery = conversacionesQuery.Where(conversacion => conversacion.nUsuarioAsignado == usuarioId.Value);
            tareasQuery = tareasQuery.Where(tarea => tarea.nAsignadoA == usuarioId.Value);
            oportunidadesQuery = oportunidadesQuery.Where(oportunidad => oportunidad.nUsuarioAsignado == usuarioId.Value);
        }

        var totalClientesNuevos = await clientesQuery.CountAsync(cliente => cliente.dFechaRegistro >= inicioHoy);
        var clientesNuevos = await clientesQuery
            .Where(cliente => cliente.dFechaRegistro >= inicioHoy)
            .OrderByDescending(cliente => cliente.dFechaRegistro)
            .Take(50)
            .Select(cliente => new
            {
                id = cliente.nCliente,
                nombre = cliente.cNombre,
                canal = cliente.cCanalOrigen,
                fecha = cliente.dFechaRegistro,
                conversacionId = cliente.Conversaciones
                    .OrderByDescending(conversacion => conversacion.dUltimoMensaje)
                    .Select(conversacion => (long?)conversacion.nConversacion)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var mensajesPendientesQuery = conversacionesQuery
            .Where(conversacion =>
                conversacion.cEstado != "CERRADO" &&
                conversacion.cEstado != "PERDIDO" &&
                conversacion.cEstado != "NO_RESPONDIO" &&
                conversacion.Mensajes.Any(mensaje =>
                    mensaje.cDireccion == 'E' &&
                    mensaje.cTipo != "comment" &&
                    mensaje.cTipo != "comment_reply") &&
                (!conversacion.Mensajes.Any(mensaje =>
                    mensaje.cDireccion == 'S' &&
                    mensaje.cTipo != "bot" &&
                    mensaje.cTipo != "comment" &&
                    mensaje.cTipo != "comment_reply") ||
                 conversacion.Mensajes
                    .Where(mensaje =>
                        mensaje.cDireccion == 'E' &&
                        mensaje.cTipo != "comment" &&
                        mensaje.cTipo != "comment_reply")
                    .Max(mensaje => (DateTime?)mensaje.dFecha) > conversacion.Mensajes
                    .Where(mensaje =>
                        mensaje.cDireccion == 'S' &&
                        mensaje.cTipo != "bot" &&
                        mensaje.cTipo != "comment" &&
                        mensaje.cTipo != "comment_reply")
                    .Max(mensaje => (DateTime?)mensaje.dFecha)));
        var totalMensajesPendientes = await mensajesPendientesQuery.CountAsync();
        var mensajesPendientes = await mensajesPendientesQuery
            .OrderByDescending(conversacion => conversacion.Mensajes
                .Where(mensaje =>
                    mensaje.cDireccion == 'E' &&
                    mensaje.cTipo != "comment" &&
                    mensaje.cTipo != "comment_reply")
                .Max(mensaje => (DateTime?)mensaje.dFecha))
            .Take(50)
            .Select(conversacion => new
            {
                id = conversacion.nConversacion,
                clienteId = conversacion.nCliente,
                nombre = conversacion.Cliente.cNombre,
                canal = conversacion.cCanal,
                estado = conversacion.cEstado,
                fecha = conversacion.Mensajes
                    .Where(mensaje =>
                        mensaje.cDireccion == 'E' &&
                        mensaje.cTipo != "comment" &&
                        mensaje.cTipo != "comment_reply")
                    .Max(mensaje => (DateTime?)mensaje.dFecha)
            })
            .ToListAsync();

        var tareasHoyQuery = tareasQuery
            .Where(tarea => tarea.cEstado == "PENDIENTE" && tarea.dFechaVencimiento < inicioManana);
        var totalTareasHoy = await tareasHoyQuery.CountAsync();
        var tareasHoy = await tareasHoyQuery
            .OrderBy(tarea => tarea.dFechaVencimiento)
            .Take(50)
            .Select(tarea => new
            {
                id = tarea.nTarea,
                titulo = tarea.cTitulo,
                vence = tarea.dFechaVencimiento,
                vencida = tarea.dFechaVencimiento < ahora,
                clienteId = tarea.nCliente,
                conversacionId = tarea.nConversacion,
                cliente = tarea.Cliente == null ? null : tarea.Cliente.cNombre
            })
            .ToListAsync();

        var oportunidadesAccionQuery = oportunidadesQuery
            .Where(oportunidad =>
                oportunidad.cEtapa != "GANADA" &&
                oportunidad.cEtapa != "PERDIDA" &&
                ((!oportunidad.dFechaActualizacion.HasValue || oportunidad.dFechaActualizacion < ahora.AddDays(-3)) ||
                 (oportunidad.dFechaCierreEstimada.HasValue && oportunidad.dFechaCierreEstimada <= limiteOportunidades) ||
                 oportunidad.cEtapa == "PROPUESTA" ||
                 oportunidad.cEtapa == "NEGOCIACION"));
        var totalOportunidadesAccion = await oportunidadesAccionQuery.CountAsync();
        var oportunidadesAccion = await oportunidadesAccionQuery
            .OrderBy(oportunidad => oportunidad.dFechaCierreEstimada ?? DateTime.MaxValue)
            .ThenBy(oportunidad => oportunidad.dFechaActualizacion ?? oportunidad.dFechaCreacion)
            .Take(50)
            .Select(oportunidad => new
            {
                id = oportunidad.nOportunidad,
                titulo = oportunidad.cTitulo,
                etapa = oportunidad.cEtapa,
                monto = oportunidad.nMonto,
                moneda = oportunidad.cMoneda,
                clienteId = oportunidad.nCliente,
                conversacionId = oportunidad.nConversacion,
                cliente = oportunidad.Cliente.cNombre,
                fechaCierreEstimada = oportunidad.dFechaCierreEstimada
            })
            .ToListAsync();

        var tareasFuturasQuery = tareasQuery
            .Where(tarea => tarea.cEstado == "PENDIENTE" && tarea.dFechaVencimiento >= limiteFuturo);
        var totalTareasFuturas = await tareasFuturasQuery.CountAsync();
        var tareasFuturas = await tareasFuturasQuery
            .OrderBy(tarea => tarea.dFechaVencimiento)
            .Take(50)
            .Select(tarea => new
            {
                id = tarea.nTarea,
                titulo = tarea.cTitulo,
                vence = tarea.dFechaVencimiento,
                clienteId = tarea.nCliente,
                conversacionId = tarea.nConversacion,
                cliente = tarea.Cliente == null ? null : tarea.Cliente.cNombre
            })
            .ToListAsync();

        return Ok(new
        {
            fecha = inicioHoy,
            usuarioId,
            hoy = new
            {
                clientesNuevos,
                mensajesPendientes,
                tareas = tareasHoy,
                oportunidades = oportunidadesAccion,
                totalClientesNuevos,
                totalMensajesPendientes,
                totalTareas = totalTareasHoy,
                totalOportunidades = totalOportunidadesAccion,
                total = totalClientesNuevos + totalMensajesPendientes + totalTareasHoy + totalOportunidadesAccion
            },
            futuro = new
            {
                tareas = tareasFuturas,
                total = totalTareasFuturas
            }
        });
    }

    [HttpGet("administracion")]
    [Authorize(Roles = "Administrador,Supervisor,Auditor")]
    public async Task<IActionResult> Administracion(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] int? usuarioId = null)
    {
        var ahora = DateTime.Now;
        var fechaDesde = (desde ?? new DateTime(ahora.Year, ahora.Month, 1)).Date;
        var fechaHasta = (hasta ?? ahora).Date;
        if (fechaDesde > fechaHasta || (fechaHasta - fechaDesde).TotalDays > 366)
        {
            return BadRequest("El rango debe ser valido y no superar 366 dias.");
        }

        if (usuarioId.HasValue && !await _context.Usuarios.AsNoTracking()
                .AnyAsync(usuario => usuario.nUsuario == usuarioId.Value && usuario.cEstado == 'A'))
        {
            return NotFound("El usuario seleccionado no existe o no esta activo.");
        }

        var fechaHastaExclusiva = fechaHasta.AddDays(1);
        var duracion = fechaHastaExclusiva - fechaDesde;
        var anteriorHastaExclusiva = fechaDesde;
        var anteriorDesde = fechaDesde - duracion;

        var oportunidades = _context.Oportunidades.AsNoTracking().AsQueryable();
        var conversaciones = _context.Conversaciones.AsNoTracking().AsQueryable();
        var tareas = _context.Tareas.AsNoTracking().AsQueryable();
        if (usuarioId.HasValue)
        {
            oportunidades = oportunidades.Where(item => item.nUsuarioAsignado == usuarioId.Value);
            conversaciones = conversaciones.Where(item => item.nUsuarioAsignado == usuarioId.Value);
            tareas = tareas.Where(item => item.nAsignadoA == usuarioId.Value);
        }

        var cierresPeriodo = oportunidades.Where(item =>
            item.dFechaCierreReal.HasValue &&
            item.dFechaCierreReal >= fechaDesde &&
            item.dFechaCierreReal < fechaHastaExclusiva);
        var cierresPeriodoDatos = await cierresPeriodo
            .GroupBy(item => item.cEtapa)
            .Select(grupo => new
            {
                etapa = grupo.Key,
                cantidad = grupo.Count(),
                monto = grupo.Sum(item => item.nMonto)
            })
            .ToListAsync();

        var ganadas = cierresPeriodoDatos.FirstOrDefault(item => item.etapa == "GANADA");
        var perdidas = cierresPeriodoDatos.FirstOrDefault(item => item.etapa == "PERDIDA");
        var ganadasCantidad = ganadas?.cantidad ?? 0;
        var perdidasCantidad = perdidas?.cantidad ?? 0;
        var montoGanado = ganadas?.monto ?? 0m;

        var montoGanadoAnterior = await oportunidades
            .Where(item => item.cEtapa == "GANADA" &&
                item.dFechaCierreReal.HasValue &&
                item.dFechaCierreReal >= anteriorDesde &&
                item.dFechaCierreReal < anteriorHastaExclusiva)
            .SumAsync(item => (decimal?)item.nMonto) ?? 0m;

        var abiertas = oportunidades.Where(item => item.cEtapa != "GANADA" && item.cEtapa != "PERDIDA");
        var limiteEstancadas = ahora.AddDays(-3);
        var resumenAbiertas = await abiertas
            .GroupBy(_ => 1)
            .Select(grupo => new
            {
                cantidad = grupo.Count(),
                monto = grupo.Sum(item => item.nMonto),
                forecast = grupo.Sum(item => item.nMonto * item.nProbabilidad / 100m),
                cierresProximos = grupo.Count(item =>
                    item.dFechaCierreEstimada.HasValue &&
                    item.dFechaCierreEstimada >= ahora.Date &&
                    item.dFechaCierreEstimada < ahora.Date.AddDays(8)),
                sinFecha = grupo.Count(item => !item.dFechaCierreEstimada.HasValue),
                estancadas = grupo.Count(item =>
                    (!item.dFechaActualizacion.HasValue && item.dFechaCreacion < limiteEstancadas) ||
                    (item.dFechaActualizacion.HasValue && item.dFechaActualizacion < limiteEstancadas))
            })
            .FirstOrDefaultAsync();
        var oportunidadesAbiertas = resumenAbiertas?.cantidad ?? 0;
        var montoAbierto = resumenAbiertas?.monto ?? 0m;
        var forecastPonderado = resumenAbiertas?.forecast ?? 0m;
        var cierresProximos = resumenAbiertas?.cierresProximos ?? 0;
        var oportunidadesSinFecha = resumenAbiertas?.sinFecha ?? 0;
        var oportunidadesEstancadas = resumenAbiertas?.estancadas ?? 0;

        var motivosPerdida = await cierresPeriodo
            .Where(item => item.cEtapa == "PERDIDA")
            .GroupBy(item => string.IsNullOrWhiteSpace(item.cMotivoPerdida) ? "Sin motivo registrado" : item.cMotivoPerdida!)
            .Select(grupo => new { motivo = grupo.Key, cantidad = grupo.Count(), monto = grupo.Sum(item => item.nMonto) })
            .OrderByDescending(item => item.cantidad)
            .Take(5)
            .ToListAsync();

        var activas = conversaciones.Where(item =>
            item.cEstado != "CERRADO" && item.cEstado != "PERDIDO" && item.cEstado != "NO_RESPONDIO");
        var pendientesRespuesta = activas.Where(item =>
            item.Mensajes.Any(mensaje =>
                mensaje.cDireccion == 'E' &&
                mensaje.cTipo != "comment" &&
                mensaje.cTipo != "comment_reply") &&
            (!item.Mensajes.Any(mensaje =>
                mensaje.cDireccion == 'S' &&
                mensaje.cTipo != "bot" &&
                mensaje.cTipo != "comment" &&
                mensaje.cTipo != "comment_reply") ||
             item.Mensajes
                .Where(mensaje =>
                    mensaje.cDireccion == 'E' &&
                    mensaje.cTipo != "comment" &&
                    mensaje.cTipo != "comment_reply")
                .Max(mensaje => (DateTime?)mensaje.dFecha) > item.Mensajes
                .Where(mensaje =>
                    mensaje.cDireccion == 'S' &&
                    mensaje.cTipo != "bot" &&
                    mensaje.cTipo != "comment" &&
                    mensaje.cTipo != "comment_reply")
                .Max(mensaje => (DateTime?)mensaje.dFecha)));
        var totalPendientesRespuesta = await pendientesRespuesta.CountAsync();
        var esperaMasAntigua = await pendientesRespuesta
            .Select(item => item.Mensajes
                .Where(mensaje =>
                    mensaje.cDireccion == 'E' &&
                    mensaje.cTipo != "comment" &&
                    mensaje.cTipo != "comment_reply")
                .Max(mensaje => (DateTime?)mensaje.dFecha))
            .MinAsync();
        var sinAsignar = await activas.CountAsync(item => !item.nUsuarioAsignado.HasValue);
        var conversacionesActivas = await activas.CountAsync();

        var resumenTareas = await tareas
            .GroupBy(_ => 1)
            .Select(grupo => new
            {
                vencidas = grupo.Count(item =>
                    item.cEstado == "PENDIENTE" && item.dFechaVencimiento < ahora),
                vencenHoy = grupo.Count(item =>
                    item.cEstado == "PENDIENTE" &&
                    item.dFechaVencimiento >= ahora &&
                    item.dFechaVencimiento < ahora.Date.AddDays(1))
            })
            .FirstOrDefaultAsync();
        var tareasVencidas = resumenTareas?.vencidas ?? 0;
        var tareasVencenHoy = resumenTareas?.vencenHoy ?? 0;

        var desdeFallos = ahora.AddHours(-24);
        var mensajesFallidos = await _context.Mensajes.AsNoTracking().CountAsync(mensaje =>
            mensaje.dFecha >= desdeFallos && mensaje.cEstado != null &&
            (mensaje.cEstado.Contains("FALLIDO") || mensaje.cEstado.Contains("ERROR") || mensaje.cEstado.Contains("LOCAL")));
        var eventosFallidos = await _context.ActividadLogs.AsNoTracking().CountAsync(log =>
            log.dFecha >= desdeFallos &&
            (log.cAccion.Contains("ERROR") || log.cAccion.Contains("FALLO")));

        decimal? variacionVentas = montoGanadoAnterior > 0
            ? Math.Round(((montoGanado - montoGanadoAnterior) / montoGanadoAnterior) * 100m, 1)
            : null;
        var cierresTotales = ganadasCantidad + perdidasCantidad;

        return Ok(new
        {
            periodo = new { desde = fechaDesde, hasta = fechaHasta, generadoEn = ahora },
            comercial = new
            {
                montoGanado,
                montoGanadoAnterior,
                variacionVentas,
                ganadas = ganadasCantidad,
                perdidas = perdidasCantidad,
                winRate = cierresTotales == 0 ? 0 : Math.Round(ganadasCantidad * 100m / cierresTotales, 1),
                oportunidadesAbiertas,
                montoAbierto,
                forecastPonderado,
                cierresProximos,
                oportunidadesSinFecha,
                oportunidadesEstancadas,
                motivosPerdida
            },
            atencion = new
            {
                conversacionesActivas,
                pendientesRespuesta = totalPendientesRespuesta,
                sinAsignar,
                esperaMasAntigua,
                minutosEsperaMaxima = esperaMasAntigua.HasValue
                    ? Math.Max(0, (int)(ahora - esperaMasAntigua.Value).TotalMinutes)
                    : 0
            },
            seguimiento = new { tareasVencidas, tareasVencenHoy },
            sistema = new { fallosUltimas24Horas = mensajesFallidos + eventosFallidos }
        });
    }
}
