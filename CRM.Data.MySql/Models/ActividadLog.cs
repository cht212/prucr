using System;

namespace CRM.Data.Models
{
    // Auditoría mínima: quién cambió qué y cuándo. Hoy el sistema no deja
    // rastro de reasignaciones, cambios de estado o edición de contactos,
    // lo cual es indispensable para que un supervisor pueda revisar el
    // trabajo del equipo.
    public class ActividadLog
    {
        public long nActividad { get; set; }
        public string cEntidad { get; set; } = string.Empty; // "Conversacion", "Cliente", "Usuario", "Oportunidad"
        public long nEntidadId { get; set; }
        public string cAccion { get; set; } = string.Empty; // "CAMBIO_ESTADO", "REASIGNACION", "EDICION", "CREACION"
        public string? cValorAnterior { get; set; }
        public string? cValorNuevo { get; set; }
        public int? nUsuario { get; set; }
        public DateTime dFecha { get; set; } = DateTime.Now;

        public CrmUsuario? Usuario { get; set; }
    }
}
