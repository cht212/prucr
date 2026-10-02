namespace CRM.Data.Models;

public sealed class CrmRolPermiso
{
    public int nRol { get; set; }
    public string cPermiso { get; set; } = string.Empty;

    public CrmRol Rol { get; set; } = null!;
}
