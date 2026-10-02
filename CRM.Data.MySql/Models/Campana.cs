using System;

namespace CRM.Data.Models
{
    public class Campana
    {
        public long nCampana { get; set; }
        public string cNombre { get; set; } = string.Empty;
        public string? cDescripcion { get; set; }
        public string cTipo { get; set; } = "WHATSAPP"; // WHATSAPP, EMAIL, SOCIAL, GENERAL
        public string cEstado { get; set; } = "ACTIVA"; // ACTIVA, PAUSADA, FINALIZADA
        public DateTime dFechaInicio { get; set; } = DateTime.Now;
        public DateTime? dFechaFin { get; set; }
        public int? nAsignadoA { get; set; }
        public int? nCreadoPor { get; set; }
        public DateTime dFechaCreacion { get; set; } = DateTime.Now;

        public CrmUsuario? AsignadoA { get; set; }
        public CrmUsuario? CreadoPor { get; set; }
    }

    public class CampanaCliente
    {
        public long nCampana { get; set; }
        public long nCliente { get; set; }
        public DateTime dFechaAsignacion { get; set; } = DateTime.Now;
        public string cEstado { get; set; } = "PENDIENTE"; // PENDIENTE, CONTACTADO, RESPONDIO, NO_RESPONDIO

        public Campana Campana { get; set; } = null!;
        public Cliente Cliente { get; set; } = null!;
    }
}
