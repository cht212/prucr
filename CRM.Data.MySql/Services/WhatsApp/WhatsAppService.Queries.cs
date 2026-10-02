using CRM.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Data.Services;

public partial class WhatsAppService
{
// =========================================================
// OBTENER CONVERSACIÓN
// =========================================================

public async Task<object?> ObtenerConversacionAsync(
    long conversacionId,
    int? usuarioAsignadoId = null,
    bool incluirDisponiblesParaTomar = false,
    IReadOnlyCollection<string>? canalesPermitidos = null,
    bool incluirFichaCliente = true,
    int pageSize = 50,
    long? antesDe = null,
    int? usuarioActualId = null,
    bool gestionarTodas = false)
{
    IQueryable<Conversacion> query =
        _context.Conversaciones
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.UsuarioAsignado)
            .Where(c => c.nConversacion == conversacionId);

    if (usuarioAsignadoId.HasValue)
    {
        query = query.Where(c =>
            c.nUsuarioAsignado == usuarioAsignadoId.Value ||
            (incluirDisponiblesParaTomar &&
             !c.nUsuarioAsignado.HasValue &&
             (c.cEstado == "NUEVO" ||
              c.cEstado == "ABIERTO" ||
              c.cEstado == "EN_ATENCION")));
    }

    if (canalesPermitidos != null)
    {
        query = query.Where(c => canalesPermitidos.Contains(c.cCanal));
    }

    var conversacion = await query.FirstOrDefaultAsync();

    if (conversacion == null)
    {
        return null;
    }

    pageSize = Math.Clamp(pageSize, 1, 100);
    var mensajesQuery = _context.Mensajes
            .AsNoTracking()
            .Where(m =>
                m.nConversacion == conversacionId &&
                m.cTipo != "comment" &&
                m.cTipo != "comment_reply");
    if (antesDe.HasValue)
    {
        var cursor = await mensajesQuery.Where(m => m.nMensaje == antesDe.Value)
            .Select(m => new { m.nMensaje, m.dFecha }).FirstOrDefaultAsync();
        if (cursor == null) return null;
        mensajesQuery = mensajesQuery.Where(m => m.dFecha < cursor.dFecha ||
            (m.dFecha == cursor.dFecha && m.nMensaje < cursor.nMensaje));
    }
    var mensajes = await mensajesQuery.OrderByDescending(m => m.dFecha).ThenByDescending(m => m.nMensaje)
        .Take(pageSize + 1).ToListAsync();
    var hayMasMensajes = mensajes.Count > pageSize;
    if (hayMasMensajes) mensajes.RemoveAt(pageSize);
    mensajes.Reverse();
    var referencias = mensajes.Where(m => m.cReplyToExternalId != null)
        .Select(m => m.cReplyToExternalId!).Distinct().ToArray();
    var originales = referencias.Length == 0 ? [] : await _context.Mensajes.AsNoTracking()
        .Where(m => m.nConversacion == conversacionId && m.cExternalId != null && referencias.Contains(m.cExternalId))
        .Take(pageSize).ToListAsync();
    var mensajesPorExternalId = mensajes.Concat(originales)
        .Where(m => !string.IsNullOrWhiteSpace(m.cExternalId))
        .GroupBy(m => m.cExternalId!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    return new
    {
        id = conversacion.nConversacion,
        hayMasMensajes,
        mensajeMasAntiguoId = mensajes.FirstOrDefault()?.nMensaje,
        puedeAtender = gestionarTodas || conversacion.nUsuarioAsignado == usuarioActualId ||
            (!conversacion.nUsuarioAsignado.HasValue && EsEstadoDisponible(conversacion.cEstado)),

        cliente = new
        {
            id = conversacion.Cliente.nCliente,

            nombre =
                conversacion.Cliente.cNombre,

            telefono =
                conversacion.Cliente.cTelefono,

            email = incluirFichaCliente ? conversacion.Cliente.cEmail : null,

            documento = incluirFichaCliente ? conversacion.Cliente.cDocumento : null,

            fotoPerfilUrl =
                conversacion.Cliente.cFotoPerfilUrl
        },

        estado =
            conversacion.cEstado,

        usuarioAsignado =
            conversacion.UsuarioAsignado == null
                ? null
                : new
                {
                    id =
                        conversacion.UsuarioAsignado.nUsuario,

                    usuario =
                        conversacion.UsuarioAsignado.cUsuario,

                    nombre =
                        conversacion.UsuarioAsignado.cNombre
                },

        fechaCreacion =
            conversacion.dFechaInicio,

        ultimoMensaje =
            conversacion.dUltimoMensaje,

        ultimoMensajeCliente =
            conversacion.dUltimoMensajeCliente,

        bot = new
        {
            estado = conversacion.cBotEstado,
            pausadoDesde = conversacion.dBotPausadoDesde,
            pausadoPor = conversacion.nBotPausadoPor
        },

        canal = conversacion.cCanal,

        requiereAsignacion =
            !conversacion.nUsuarioAsignado.HasValue &&
            conversacion.cEstado is "NUEVO" or "ABIERTO" or "EN_ATENCION",

        mensajes = mensajes.Select(m =>
        {
            mensajesPorExternalId.TryGetValue(m.cReplyToExternalId ?? string.Empty, out var mensajeRespondido);
            return new
            {
                id = m.nMensaje,

                whatsappId =
                    m.cWhatsappId,

                canal =
                    m.cCanal,

                externalId =
                    m.cExternalId,

                replyToExternalId =
                    m.cReplyToExternalId,

                respuestaA = mensajeRespondido == null
                    ? null
                    : new
                    {
                        id = mensajeRespondido.nMensaje,
                        direccion = mensajeRespondido.cDireccion,
                        tipo = mensajeRespondido.cTipo,
                        mensaje = mensajeRespondido.cMensaje
                    },

                direccion =
                    m.cDireccion,

                tipo =
                    m.cTipo,

                estado =
                    m.cEstado,

                mensaje =
                    m.cMensaje,

                fecha =
                    m.dFecha
            };
        })
    };
}

// =========================================================
// TODAS LAS CONVERSACIONES
// =========================================================

public async Task<object> ObtenerTodasConversacionesAsync(
    int? usuarioAsignadoId = null,
    bool incluirDisponiblesParaTomar = false,
    IReadOnlyCollection<string>? canalesPermitidos = null,
    int page = 1,
    int pageSize = 50,
    string? canal = null,
    string? search = null,
    string? filtro = null,
    string? asesor = null,
    int? usuarioActualId = null)
{
    IQueryable<Conversacion> query =
        _context.Conversaciones
            .AsNoTracking()
            .Include(c => c.Cliente)
            .Include(c => c.UsuarioAsignado)
            .Where(c => c.Mensajes.Any(m =>
                m.cTipo != "comment" &&
                m.cTipo != "comment_reply"));

    if (usuarioAsignadoId.HasValue)
    {
        query = query.Where(c =>
            c.nUsuarioAsignado == usuarioAsignadoId.Value ||
            (incluirDisponiblesParaTomar &&
             !c.nUsuarioAsignado.HasValue &&
             (c.cEstado == "NUEVO" ||
              c.cEstado == "ABIERTO" ||
              c.cEstado == "EN_ATENCION")));
    }


    if (canalesPermitidos != null)
    {
        query = query.Where(c => canalesPermitidos.Contains(c.cCanal));
    }

    var pendientes = await query.Where(c => c.nUsuarioAsignado == null || c.nUsuarioAsignado == usuarioActualId)
        .CountAsync(c => c.Mensajes.Any(m => m.cDireccion == 'E' && m.cTipo != "comment" && m.cTipo != "comment_reply" &&
            !c.Mensajes.Any(s => s.cDireccion == 'S' && s.cTipo != "bot" && s.cTipo != "comment" && s.cTipo != "comment_reply" && s.dFecha >= m.dFecha)));
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 100);
    if (!string.IsNullOrWhiteSpace(canal) && canal != "TODOS") query = query.Where(c => c.cCanal == canal);
    if (!string.IsNullOrWhiteSpace(search))
    {
        var termino = search.Trim();
        query = query.Where(c => c.Cliente.cNombre.Contains(termino) || c.Cliente.cTelefono.Contains(termino));
    }
    if (asesor == "unassigned") query = query.Where(c => c.nUsuarioAsignado == null);
    else if (int.TryParse(asesor, out var asesorId)) query = query.Where(c => c.nUsuarioAsignado == asesorId);
    query = filtro switch
    {
        "new" => query.Where(c => c.cEstado == "NUEVO"),
        "open" => query.Where(c => c.cEstado == "ABIERTO" || c.cEstado == "EN_ATENCION"),
        "mine" => query.Where(c => c.nUsuarioAsignado == usuarioActualId),
        "unassigned" => query.Where(c => c.nUsuarioAsignado == null),
        "pending" => query.Where(c => c.Mensajes.Any(m => m.cDireccion == 'E' && m.cTipo != "comment" && m.cTipo != "comment_reply") &&
            c.Mensajes.Where(m => m.cDireccion == 'E' && m.cTipo != "comment" && m.cTipo != "comment_reply").Max(m => (DateTime?)m.dFecha) >
            (c.Mensajes.Where(m => m.cDireccion == 'S' && m.cTipo != "bot" && m.cTipo != "comment" && m.cTipo != "comment_reply").Max(m => (DateTime?)m.dFecha) ?? DateTime.MinValue)),
        _ => query
    };
    var total = await query.CountAsync();
    page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));

    var conversaciones =
        await query
            .OrderByDescending(
                c => c.dUltimoMensaje)
            .ThenByDescending(c => c.nConversacion)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                id =
                    c.nConversacion,

                clienteId =
                    c.nCliente,

                telefono =
                    c.Cliente.cTelefono,

                nombre =
                    c.Cliente.cNombre,

                fotoPerfilUrl =
                    c.Cliente.cFotoPerfilUrl,

                estado =
                    c.cEstado,

                botEstado =
                    c.cBotEstado,

                canal =
                    c.cCanal,

                usuarioAsignadoId =
                    c.nUsuarioAsignado,

                usuarioAsignado =
                    c.UsuarioAsignado == null
                        ? null
                        : c.UsuarioAsignado.cNombre,

                requiereAsignacion =
                    !c.nUsuarioAsignado.HasValue &&
                    (c.cEstado == "NUEVO" || c.cEstado == "ABIERTO" || c.cEstado == "EN_ATENCION"),

                ultimoMensaje =
                    c.Mensajes
                        .Where(m => m.cTipo != "comment" && m.cTipo != "comment_reply")
                        .Max(m => (DateTime?)m.dFecha),

                ultimoMensajeTexto =
                    c.Mensajes
                        .Where(m => m.cTipo != "comment" && m.cTipo != "comment_reply")
                        .OrderByDescending(m => m.dFecha)
                        .ThenByDescending(m => m.nMensaje)
                        .Select(m => m.cMensaje)
                        .FirstOrDefault(),

                ultimoMensajeTipo =
                    c.Mensajes
                        .Where(m => m.cTipo != "comment" && m.cTipo != "comment_reply")
                        .OrderByDescending(m => m.dFecha)
                        .ThenByDescending(m => m.nMensaje)
                        .Select(m => m.cTipo)
                        .FirstOrDefault(),

                ultimoMensajeDireccion =
                    c.Mensajes
                        .Where(m => m.cTipo != "comment" && m.cTipo != "comment_reply")
                        .OrderByDescending(m => m.dFecha)
                        .ThenByDescending(m => m.nMensaje)
                        .Select(m => (char?)m.cDireccion)
                        .FirstOrDefault(),

                ultimoMensajeCliente =
                    c.Mensajes
                        .Where(m =>
                            m.cDireccion == 'E' &&
                            m.cTipo != "comment" &&
                            m.cTipo != "comment_reply")
                        .Max(m => (DateTime?)m.dFecha),

                requiereAtencion =
                    c.Mensajes.Any(m =>
                        m.cDireccion == 'E' &&
                        m.cTipo != "comment" &&
                        m.cTipo != "comment_reply") &&
                    (
                        !c.Mensajes.Any(m =>
                            m.cDireccion == 'S' &&
                            m.cTipo != "bot" &&
                            m.cTipo != "comment" &&
                            m.cTipo != "comment_reply") ||
                        c.Mensajes
                            .Where(m =>
                                m.cDireccion == 'E' &&
                                m.cTipo != "comment" &&
                                m.cTipo != "comment_reply")
                            .Max(m => (DateTime?)m.dFecha) >
                        c.Mensajes
                            .Where(m =>
                                m.cDireccion == 'S' &&
                                m.cTipo != "bot" &&
                                m.cTipo != "comment" &&
                                m.cTipo != "comment_reply")
                            .Max(m => (DateTime?)m.dFecha)
                    )
            })
            .ToListAsync();

    return new { total, page, pageSize, pendientes, items = conversaciones };
}

private static bool EsEstadoDisponible(string estado) => estado is "NUEVO" or "ABIERTO" or "EN_ATENCION";
}
