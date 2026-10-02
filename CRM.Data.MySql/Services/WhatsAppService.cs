using CRM.Data.Data;
using CRM.Data.Models;

namespace CRM.Data.Services;

public partial class WhatsAppService
{
    private readonly CrmDbContext _context;
    private readonly WhatsAppCloudApiService _whatsAppCloudApiService;
    private readonly MetaMessagingService _metaMessagingService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BotSettingsService _botSettings;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly WhatsAppNumberRegistry _numbers;

    public WhatsAppService(
        CrmDbContext context,
        WhatsAppCloudApiService whatsAppCloudApiService,
        MetaMessagingService metaMessagingService,
        IServiceScopeFactory scopeFactory,
        BotSettingsService botSettings,
        WhatsAppNumberRegistry numbers,
        ILogger<WhatsAppService> logger)
    {
        _context = context;
        _whatsAppCloudApiService = whatsAppCloudApiService;
        _metaMessagingService = metaMessagingService;
        _scopeFactory = scopeFactory;
        _botSettings = botSettings;
        _logger = logger;
        _numbers = numbers;
    }
}
