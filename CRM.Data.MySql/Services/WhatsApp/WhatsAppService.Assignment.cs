using CRM.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace CRM.Data.Services;

public partial class WhatsAppService
{
// =========================================================
// ASIGNAR PENDIENTES (BACKFILL)
// =========================================================
//
// Recorre las conversaciones NUEVO/ABIERTO/EN_ATENCION que quedaron sin
// asesor (por ejemplo, las creadas antes de activar el
// reparto automático) y las asigna una por una, respetando
// el balance de carga. Se usa desde el botón "Asignar
// pendientes" de Leads, y solo hace falta ejecutarlo
// una vez: los mensajes nuevos ya se asignan solos.
//
// =========================================================

public async Task<int> AsignarConversacionesPendientesAsync()
{
    await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    var pendientes = await _context.Conversaciones
        .Where(c =>
            !c.nUsuarioAsignado.HasValue &&
            (c.cEstado == "NUEVO" || c.cEstado == "ABIERTO" || c.cEstado == "EN_ATENCION"))
        .OrderBy(c => c.dFechaInicio)
        .ToListAsync();

    if (pendientes.Count == 0)
    {
        return 0;
    }

    var asesoresActivos = await ObtenerAsesoresActivosAsync();

    if (asesoresActivos.Count == 0)
    {
        _logger.LogWarning(
            "No hay asesores activos disponibles para asignar las conversaciones pendientes.");

        return 0;
    }

    // ---------------------------------------------------
    // Carga inicial (una sola consulta) y luego se va
    // actualizando en memoria conforme se reparte cada
    // conversación, para que el balance sea correcto
    // dentro del mismo lote.
    // ---------------------------------------------------

    var cargaPorAsesor = await ObtenerCargaPorAsesorAsync(asesoresActivos, excluirConversacionId: null);

    foreach (var conversacion in pendientes)
    {
        int asesorElegido = ElegirAsesorConMenosCarga(asesoresActivos, cargaPorAsesor);

        conversacion.nUsuarioAsignado = asesorElegido;

        cargaPorAsesor[asesorElegido] =
            cargaPorAsesor.TryGetValue(asesorElegido, out var actual) ? actual + 1 : 1;
    }

    await _context.SaveChangesAsync();
    await transaction.CommitAsync();

    _logger.LogInformation(
        "Asignación automática de pendientes ejecutada. " +
        "Conversaciones asignadas: {Cantidad}",
        pendientes.Count);

    return pendientes.Count;
}

// =========================================================
// ASIGNACIÓN AUTOMÁTICA (ESTILO KOMMO)
// =========================================================
//
// Reparte la conversación entre los asesores/supervisores
// activos, eligiendo siempre al que tenga menos
// conversaciones abiertas (NUEVO, ABIERTO o EN_ATENCION) en ese momento.
// Esto evita que un solo asesor se sature y elimina la
// necesidad de asignar manualmente cada lead nuevo.
//
// =========================================================

private async Task AsignarAutomaticamenteAsync(
    Conversacion conversacion)
{
    await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    var asesoresActivos = await ObtenerAsesoresActivosAsync();

    if (asesoresActivos.Count == 0)
    {
        _logger.LogWarning(
            "No hay asesores activos disponibles para " +
            "la asignación automática de la conversación {ConversacionId}.",
            conversacion.nConversacion);

        return;
    }

    var cargaPorAsesor = await ObtenerCargaPorAsesorAsync(
        asesoresActivos,
        excluirConversacionId: conversacion.nConversacion);

    int asesorElegido = ElegirAsesorConMenosCarga(asesoresActivos, cargaPorAsesor);

    conversacion.nUsuarioAsignado = asesorElegido;

    await _context.SaveChangesAsync();
    await transaction.CommitAsync();

    _logger.LogInformation(
        "Conversación {ConversacionId} asignada automáticamente al asesor {UsuarioId}.",
        conversacion.nConversacion,
        asesorElegido);
}

// ---------------------------------------------------
// Usuarios activos que pueden recibir conversaciones
// ---------------------------------------------------

private async Task<List<int>> ObtenerAsesoresActivosAsync()
{
    return await _context.Usuarios
        .AsNoTracking()
        .Where(u =>
            u.cEstado == 'A' &&
            (u.cRol == "Asesor" || u.cRol == "Supervisor"))
        .Select(u => u.nUsuario)
        .ToListAsync();
}

// ---------------------------------------------------
// Cuántas conversaciones activas (NUEVO/ABIERTO/EN_ATENCION) tiene
// cada asesor en este momento
// ---------------------------------------------------

private async Task<Dictionary<int, int>> ObtenerCargaPorAsesorAsync(
    List<int> asesoresActivos,
    long? excluirConversacionId)
{
    return await _context.Conversaciones
        .AsNoTracking()
        .Where(c =>
            c.nUsuarioAsignado.HasValue &&
            asesoresActivos.Contains(c.nUsuarioAsignado.Value) &&
            (c.cEstado == "NUEVO" || c.cEstado == "ABIERTO" || c.cEstado == "EN_ATENCION") &&
            (!excluirConversacionId.HasValue || c.nConversacion != excluirConversacionId.Value))
        .GroupBy(c => c.nUsuarioAsignado!.Value)
        .Select(g => new { UsuarioId = g.Key, Cantidad = g.Count() })
        .ToDictionaryAsync(x => x.UsuarioId, x => x.Cantidad);
}

// ---------------------------------------------------
// Elige al asesor con menor carga (empate: menor Id,
// para que el reparto sea determinístico)
// ---------------------------------------------------

private static int ElegirAsesorConMenosCarga(
    List<int> asesoresActivos,
    Dictionary<int, int> cargaPorAsesor)
{
    return asesoresActivos
        .OrderBy(id => cargaPorAsesor.TryGetValue(id, out var cantidad) ? cantidad : 0)
        .ThenBy(id => id)
        .First();
}
}
