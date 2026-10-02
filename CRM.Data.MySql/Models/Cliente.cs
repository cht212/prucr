using System;
using System.Collections.Generic;

namespace CRM.Data.Models
{
    public class Cliente
    {
        public long nCliente { get; set; }

        public string cNombre { get; set; } = string.Empty;

        public string cTelefono { get; set; } = string.Empty;

        public string? cEmail { get; set; }

        public string? cDocumento { get; set; }

        public string? cFotoPerfilUrl { get; set; }

        public string cCanalOrigen { get; set; } = CanalSocial.WhatsApp;

        public DateTime dFechaRegistro { get; set; } = DateTime.Now;

        public char cEstado { get; set; } = 'A';

        // Relación: Un cliente tiene muchas conversaciones
        public ICollection<Conversacion> Conversaciones { get; set; } = new List<Conversacion>();
    }
}
