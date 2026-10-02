namespace CRM.Data.DTOs.Social;

public sealed class SocialInboundTestDto
{
    public string ExternalUserId { get; set; } = string.Empty;
    public string ContactValue { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Type { get; set; } = "text";
    public string? ExternalMessageId { get; set; }
}
