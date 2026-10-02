using CRM.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Data.Services;

public partial class WhatsAppService
{
public async Task CambiarEstadoBotConversacionAsync(
    long conversacionId,
    string estado,
    int? usuarioId)
{
    estado = estado.Trim().ToUpperInvariant();
    if (estado is not "ACTIVO" and not "PAUSADO")
    {
        throw new InvalidOperationException("Estado de bot no valido.");
    }

    var conversacion = await _context.Conversaciones
        .FirstOrDefaultAsync(c => c.nConversacion == conversacionId);

    if (conversacion == null)
    {
        throw new InvalidOperationException("La conversacion no existe.");
    }

    conversacion.cBotEstado = estado;
    conversacion.dBotPausadoDesde = estado == "PAUSADO" ? DateTime.Now : null;
    conversacion.nBotPausadoPor = estado == "PAUSADO" ? usuarioId : null;

    await _context.SaveChangesAsync();
}

public async Task<int> ReactivarBotConversacionesPausadasAsync()
{
    var conversaciones = await _context.Conversaciones
        .Where(c =>
            (c.cCanal == CanalSocial.WhatsApp ||
             c.cCanal == CanalSocial.Facebook ||
             c.cCanal == CanalSocial.Instagram) &&
            c.cBotEstado == "PAUSADO" &&
            (c.cEstado == "NUEVO" ||
             c.cEstado == "ABIERTO" ||
             c.cEstado == "EN_ATENCION"))
        .ToListAsync();

    foreach (var conversacion in conversaciones)
    {
        conversacion.cBotEstado = "ACTIVO";
        conversacion.dBotPausadoDesde = null;
        conversacion.nBotPausadoPor = null;
    }

    await _context.SaveChangesAsync();
    return conversaciones.Count;
}
}
