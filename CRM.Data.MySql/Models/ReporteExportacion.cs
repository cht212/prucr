namespace CRM.Data.Models
{
    public class ReporteExportacion
    {
        public long nReporte { get; set; }
        public string cNombre { get; set; } = string.Empty;
        public string cTipo { get; set; } = "CSV";
        public string cEntidad { get; set; } = "GENERAL";
        public string cRuta { get; set; } = string.Empty;
        public DateTime dFechaCreacion { get; set; } = DateTime.Now;
        public int? nCreadoPor { get; set; }

        public CrmUsuario? CreadoPor { get; set; }
    }
}
