using CRM.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Data.Services;

public partial class WhatsAppService
{
// =========================================================
// MOSTRAR "ESCRIBIENDO..." AL CLIENTE
// =========================================================
//
// Se llama cuando el asesor empieza a teclear en el chat.
// Busca el último mensaje que el cliente envió en esa
// conversación (necesitamos su whatsappId para el acuse de
// lectura + typing indicator) y se lo pide a Meta.
//
// Si no hay integración con Meta activa (modo prueba local)
// simplemente no hace nada.
//
// =========================================================

public async Task<bool> MostrarEscribiendoAsync(long conversacionId)
{
    if (!_whatsAppCloudApiService.ShouldSendToMeta)
    {
        return false;
    }

    var ultimoEntrante = await _context.Mensajes
        .AsNoTracking()
        .Where(m =>
            m.nConversacion == conversacionId &&
            m.cDireccion == 'E' &&
            m.cWhatsappId != null)
        .OrderByDescending(m => m.dFecha)
        .FirstOrDefaultAsync();

    if (ultimoEntrante?.cWhatsappId == null)
    {
        return false;
    }

    var originPhoneNumberId = await _context.Conversaciones.AsNoTracking()
        .Where(c => c.nConversacion == conversacionId)
        .Select(c => c.cPhoneNumberId).FirstOrDefaultAsync();
    return await _whatsAppCloudApiService.MostrarEscribiendoAsync(
        ultimoEntrante.cWhatsappId, originPhoneNumberId);
}
}
