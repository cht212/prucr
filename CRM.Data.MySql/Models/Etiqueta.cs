using System.Collections.Generic;

namespace CRM.Data.Models
{
    public class Etiqueta
    {
        public int nEtiqueta { get; set; }
        public string cNombre { get; set; } = string.Empty;
        public string cColor { get; set; } = "#6366F1";

        // Relación muchos-a-muchos con Cliente
        public ICollection<ClienteEtiqueta> Clientes { get; set; } = new List<ClienteEtiqueta>();
    }

    // Tabla intermedia explícita (permite auditar quién y cuándo etiquetó)
    public class ClienteEtiqueta
    {
        public long nCliente { get; set; }
        public int nEtiqueta { get; set; }
        public DateTime dFechaAsignacion { get; set; } = DateTime.Now;

        public Cliente Cliente { get; set; } = null!;
        public Etiqueta Etiqueta { get; set; } = null!;
    }
}
