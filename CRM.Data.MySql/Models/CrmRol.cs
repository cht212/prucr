namespace CRM.Data.Models;

public sealed class CrmRol
{
    public int nRol { get; set; }
    public string cNombre { get; set; } = string.Empty;
    public string cDescripcion { get; set; } = string.Empty;
    public string cRolBase { get; set; } = "Asesor";
    public char cEstado { get; set; } = 'A';
    public DateTime dFechaCreacion { get; set; } = DateTime.UtcNow;
    public int? nCreadoPor { get; set; }

    public CrmUsuario? CreadoPor { get; set; }
    public ICollection<CrmUsuario> Usuarios { get; set; } = new List<CrmUsuario>();
    public ICollection<CrmRolPermiso> Permisos { get; set; } = new List<CrmRolPermiso>();
}
