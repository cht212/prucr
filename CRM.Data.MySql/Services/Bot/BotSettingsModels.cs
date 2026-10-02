namespace CRM.Data.Services;

public sealed record BotOption(
    string Key,
    string Title,
    string Response,
    bool DerivesToAdvisor);

public sealed class BotSettingsFile
{
    public bool Enabled { get; set; }
    public string? Message { get; set; }
    public int? MaxAutoReplies { get; set; }
    public List<BotOption>? Options { get; set; }
}
