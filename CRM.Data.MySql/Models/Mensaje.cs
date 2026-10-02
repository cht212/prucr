using System;

namespace CRM.Data.Models
{
    public class Mensaje
    {
        public long nMensaje { get; set; }
        public long nConversacion { get; set; }
        public string? cWhatsappId { get; set; }
        public string cCanal { get; set; } = CanalSocial.WhatsApp;
        public string? cExternalId { get; set; }
        public string? cReplyToExternalId { get; set; }
        public string? cClientRequestId { get; set; }
        public char cDireccion { get; set; } // 'E' = Entrante, 'S' = Saliente
        public string? cTipo { get; set; }
        public string cMensaje { get; set; } = string.Empty;
        public string? cEstado { get; set; }
        public DateTime dFecha { get; set; } = DateTime.Now;

        // Relación
        public Conversacion Conversacion { get; set; } = null!;
    }
}
