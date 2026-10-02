namespace CRM.Data.Models
{
    public class ReglaAutomatica
    {
        public long nRegla { get; set; }
        public string cNombre { get; set; } = string.Empty;
        public string cEntidad { get; set; } = "Oportunidad";
        public string cEvento { get; set; } = "CAMBIO_ETAPA";
        public string cCondicion { get; set; } = string.Empty;
        public string cAccion { get; set; } = "CREAR_TAREA";
        public string? cValorAccion { get; set; }
        public int? nAsignadoA { get; set; }
        public int? nCreadoPor { get; set; }
        public bool bActiva { get; set; } = true;
        public DateTime dFechaCreacion { get; set; } = DateTime.Now;

        public CrmUsuario? AsignadoA { get; set; }
        public CrmUsuario? CreadoPor { get; set; }
    }
}
