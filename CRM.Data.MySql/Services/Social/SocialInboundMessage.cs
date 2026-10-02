namespace CRM.Data.Services;

public sealed record SocialInboundMessage(
    string Canal,
    string ExternalUserId,
    string ContactValue,
    string? DisplayName,
    string Text,
    string? Type,
    string? ExternalMessageId,
    string? ProfilePictureUrl = null,
    bool AllowBotReply = true,
    string? ParentExternalId = null,
    DateTimeOffset? OccurredAt = null);
