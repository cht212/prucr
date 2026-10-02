namespace CRM.Data.DTOs.Bot;

public sealed class BotWhatsAppSettingsDto
{
    public bool Enabled { get; set; }
    public string? Message { get; set; }
    public int? MaxAutoReplies { get; set; }
    public List<BotOptionDto>? Options { get; set; }
}
