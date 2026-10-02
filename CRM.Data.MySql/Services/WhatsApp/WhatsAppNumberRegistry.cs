using System.Text.Json;

namespace CRM.Data.Services;

public sealed record WhatsAppNumber(string PhoneNumberId, string? DisplayNumber);

public sealed class WhatsAppNumberRegistry
{
    private readonly IConfiguration _configuration;
    private readonly SocialIntegrationService _integrations;

    public WhatsAppNumberRegistry(IConfiguration configuration, SocialIntegrationService integrations)
    {
        _configuration = configuration;
        _integrations = integrations;
    }

    public IReadOnlyList<WhatsAppNumber> GetNumbers()
    {
        var raw = Get("WhatsApp:Numbers");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                var numbers = JsonSerializer.Deserialize<List<WhatsAppNumber>>(raw,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                return numbers.Where(n => !string.IsNullOrWhiteSpace(n.PhoneNumberId))
                    .GroupBy(n => n.PhoneNumberId, StringComparer.Ordinal).Select(g => g.First()).ToArray();
            }
            catch (JsonException)
            {
                return [];
            }
        }

        var legacy = Get("WhatsApp:PhoneNumberId");
        return string.IsNullOrWhiteSpace(legacy) ? [] : [new WhatsAppNumber(legacy, null)];
    }

    public string? DefaultPhoneNumberId => GetNumbers().FirstOrDefault()?.PhoneNumberId;

    public bool Contains(string? phoneNumberId) =>
        !string.IsNullOrWhiteSpace(phoneNumberId) &&
        GetNumbers().Any(n => n.PhoneNumberId == phoneNumberId);

    private string? Get(string key) => _integrations.GetConfiguredValue(key) ?? _configuration[key];
}
