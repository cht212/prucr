using System;

namespace CRM.Data.Models
{
    // Recordatorios/seguimientos ("llamar el jueves", "enviar cotización").
    // No existía ninguna entidad de este tipo en el modelo original.
    public class Tarea
    {
        public long nTarea { get; set; }
        public long? nCliente { get; set; }
        public long? nConversacion { get; set; }
        public long? nOportunidad { get; set; }

        public string cTitulo { get; set; } = string.Empty;
        public string? cDescripcion { get; set; }
        public DateTime dFechaVencimiento { get; set; }
        public string cEstado { get; set; } = "PENDIENTE"; // PENDIENTE, COMPLETADA, VENCIDA, CANCELADA

        public int? nAsignadoA { get; set; }
        public int? nCreadoPor { get; set; }
        public DateTime dFechaCreacion { get; set; } = DateTime.Now;
        public DateTime? dFechaCompletada { get; set; }

        public Cliente? Cliente { get; set; }
        public Conversacion? Conversacion { get; set; }
        public Oportunidad? Oportunidad { get; set; }
        public CrmUsuario? AsignadoA { get; set; }
    }
}
