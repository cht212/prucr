using System;
using System.Collections.Generic;

namespace CRM.Data.Models
{
    public class Conversacion
    {
        public long nConversacion { get; set; }
        public long nCliente { get; set; }
        public int? nUsuarioAsignado { get; set; }
        public string cEstado { get; set; } = "NUEVO";
        public string cCanal { get; set; } = CanalSocial.WhatsApp;
        public string? cExternalThreadId { get; set; }
        public string? cPhoneNumberId { get; set; }
        public string cBotEstado { get; set; } = "ACTIVO";
        public DateTime dFechaInicio { get; set; } = DateTime.Now;
        public DateTime? dUltimoMensaje { get; set; }
        public DateTime? dUltimoMensajeCliente { get; set; }
        public DateTime? dBotPausadoDesde { get; set; }
        public int? nBotPausadoPor { get; set; }

        // Relaciones
        public Cliente Cliente { get; set; } = null!;
        public CrmUsuario? UsuarioAsignado { get; set; }
        public ICollection<Mensaje> Mensajes { get; set; } = new List<Mensaje>();
    }
}
