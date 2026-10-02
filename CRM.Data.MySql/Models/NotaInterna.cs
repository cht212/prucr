using System;

namespace CRM.Data.Models
{
    // Nota interna del equipo sobre un cliente/conversación. Nunca se
    // envía por WhatsApp: es contexto privado para el equipo de ventas
    // (ej. "cliente pidió que lo llamen después de las 6pm").
    public class NotaInterna
    {
        public long nNota { get; set; }
        public long nCliente { get; set; }
        public long? nConversacion { get; set; }
        public string cTexto { get; set; } = string.Empty;
        public int nCreadoPor { get; set; }
        public DateTime dFecha { get; set; } = DateTime.Now;

        public Cliente Cliente { get; set; } = null!;
        public Conversacion? Conversacion { get; set; }
        public CrmUsuario CreadoPor { get; set; } = null!;
    }
}
