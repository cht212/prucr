using CRM.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Data.Services;

public partial class WhatsAppService
{
// =========================================================
// ACTUALIZAR ESTADO DE MENSAJE (checks de WhatsApp)
// =========================================================
//
// Traduce el estado que manda Meta (sent/delivered/read/failed)
// a nuestro estado en español y lo guarda en el mensaje
// saliente correspondiente (identificado por c_whatsapp_id).
//
// Como los acuses pueden llegar desordenados o repetidos,
// solo avanzamos el estado hacia adelante en la escala:
// ENVIADO -> ENTREGADO -> LEIDO. FALLIDO siempre se aplica.
//
// =========================================================

private static readonly Dictionary<string, int> RangoEstado = new()
{
    ["ENVIADO"] = 1,
    ["ENTREGADO"] = 2,
    ["LEIDO"] = 3
};

public async Task ActualizarEstadoMensajeAsync(
    string whatsappId,
    string estadoMeta,
    string? detalleError = null)
{
    string estadoNuevo = estadoMeta.Trim().ToLowerInvariant() switch
    {
        "sent" => "ENVIADO",
        "delivered" => "ENTREGADO",
        "read" => "LEIDO",
        "failed" => "FALLIDO",
        _ => "ENVIADO"
    };

    var mensaje = await _context.Mensajes
        .FirstOrDefaultAsync(m =>
            m.cWhatsappId == whatsappId &&
            m.cDireccion == 'S');

    if (mensaje == null)
    {
        _logger.LogWarning(
            "No se encontró el mensaje saliente para actualizar su estado. WhatsappId: {WhatsappId}",
            whatsappId);
        return;
    }

    if (estadoNuevo == "FALLIDO")
    {
        mensaje.cEstado = string.IsNullOrWhiteSpace(detalleError)
            ? "FALLIDO"
            : $"FALLIDO:{detalleError}";
    }
    else if (
        !RangoEstado.TryGetValue(mensaje.cEstado ?? "", out var actual) ||
        RangoEstado.TryGetValue(estadoNuevo, out var nuevo) && nuevo > actual)
    {
        mensaje.cEstado = estadoNuevo;
    }
    else
    {
        // El acuse llegó desordenado o repetido; no retrocedemos.
        return;
    }

    await _context.SaveChangesAsync();

    _logger.LogInformation(
        "Estado de mensaje actualizado. WhatsappId: {WhatsappId}, Estado: {Estado}",
        whatsappId,
        mensaje.cEstado);
}
}
