using System;

namespace CRM.Data.Models
{
    // Representa una venta potencial ligada a un cliente (y opcionalmente
    // a la conversación de WhatsApp donde nació). Es lo que le falta al
    // "pipeline" actual para ser un embudo de ventas real: monto, etapa,
    // probabilidad y fecha estimada de cierre, en vez de solo el estado
    // de atención de la conversación.
    public class Oportunidad
    {
        public long nOportunidad { get; set; }
        public long nCliente { get; set; }
        public long? nConversacion { get; set; }
        public int? nUsuarioAsignado { get; set; }

        public string cTitulo { get; set; } = string.Empty;
        public decimal nMonto { get; set; }
        public string cMoneda { get; set; } = "PEN";

        // Etapas del embudo de ventas (distintas del estado de la conversación)
        public string cEtapa { get; set; } = "NUEVA"; // NUEVA, CALIFICADA, PROPUESTA, NEGOCIACION, GANADA, PERDIDA
        public int nProbabilidad { get; set; } = 10; // 0-100
        public DateTime? dFechaCierreEstimada { get; set; }
        public DateTime? dFechaCierreReal { get; set; }
        public string? cMotivoPerdida { get; set; }

        public DateTime dFechaCreacion { get; set; } = DateTime.Now;
        public DateTime? dFechaActualizacion { get; set; }
        public int? nCreadoPor { get; set; }

        public Cliente Cliente { get; set; } = null!;
        public Conversacion? Conversacion { get; set; }
        public CrmUsuario? UsuarioAsignado { get; set; }
    }
}
