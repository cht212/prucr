using CRM.Data.Data;
using CRM.Data.Models;

namespace CRM.Data.Services
{
    // Centraliza el registro de auditoría para que cada controlador no
    // tenga que construir el ActividadLog a mano. Se usa desde
    // CrmManagementController y OportunidadesController.
    public class AuditoriaService
    {
        private readonly CrmDbContext _context;

        public AuditoriaService(CrmDbContext context)
        {
            _context = context;
        }

        public async Task RegistrarAsync(
            string entidad,
            long entidadId,
            string accion,
            string? valorAnterior,
            string? valorNuevo,
            int? usuarioId)
        {
            _context.ActividadLogs.Add(new ActividadLog
            {
                cEntidad = entidad,
                nEntidadId = entidadId,
                cAccion = accion,
                cValorAnterior = valorAnterior,
                cValorNuevo = valorNuevo,
                nUsuario = usuarioId,
                dFecha = DateTime.Now
            });

            await _context.SaveChangesAsync();
        }
    }
}
