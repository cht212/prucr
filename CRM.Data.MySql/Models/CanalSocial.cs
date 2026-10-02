namespace CRM.Data.Models;

public static class CanalSocial
{
    public const string WhatsApp = "WHATSAPP";
    public const string Instagram = "INSTAGRAM";
    public const string Facebook = "FACEBOOK";
    public const string TikTok = "TIKTOK";

    public static readonly string[] Soportados =
    [
        WhatsApp,
        Instagram,
        Facebook,
        TikTok
    ];

    public static string Normalizar(string? canal)
    {
        var valor = (canal ?? WhatsApp).Trim().ToUpperInvariant();
        return Soportados.Contains(valor) ? valor : WhatsApp;
    }
}
