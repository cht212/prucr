namespace CRM.Data.Models
{
    public class KpiDashboard
    {
        public long nKpi { get; set; }
        public string cNombre { get; set; } = string.Empty;
        public string cTipo { get; set; } = "GENERAL";
        public string cValor { get; set; } = "0";
        public string? cMeta { get; set; }
        public string cPeriodo { get; set; } = "HOY";
        public DateTime dFechaCreacion { get; set; } = DateTime.Now;
    }
}
