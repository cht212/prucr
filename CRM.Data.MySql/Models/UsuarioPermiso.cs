namespace CRM.Data.Models;

public sealed class UsuarioPermiso
{
    public int nUsuario { get; set; }
    public string cPermiso { get; set; } = string.Empty;
    public DateTime dFechaAsignacion { get; set; } = DateTime.UtcNow;
    public int? nOtorgadoPor { get; set; }

    public CrmUsuario Usuario { get; set; } = null!;
    public CrmUsuario? OtorgadoPor { get; set; }
}
