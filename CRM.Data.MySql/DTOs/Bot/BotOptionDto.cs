using CRM.Data.Services;

namespace CRM.Data.DTOs.Bot;

public sealed class BotOptionDto
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public bool DerivesToAdvisor { get; set; } = true;

    public static implicit operator BotOption(BotOptionDto dto) =>
        new(dto.Key, dto.Title, dto.Response, dto.DerivesToAdvisor);
}
