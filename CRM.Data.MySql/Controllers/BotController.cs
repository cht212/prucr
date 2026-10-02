using CRM.Data.DTOs.Bot;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/bot")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public sealed class BotController : ControllerBase
{
    private readonly BotSettingsService _botSettings;
    private readonly WhatsAppService _whatsAppService;

    public BotController(
        BotSettingsService botSettings,
        WhatsAppService whatsAppService)
    {
        _botSettings = botSettings;
        _whatsAppService = whatsAppService;
    }

    [HttpGet("whatsapp")]
    [CrmPermission(CrmPermissionService.ModuleBot)]
    public IActionResult GetWhatsApp()
    {
        return Ok(new
        {
            enabled = _botSettings.WhatsAppAutoReplyEnabled,
            message = _botSettings.WhatsAppAutoReplyMessage,
            maxAutoReplies = _botSettings.MaxAutoRepliesPerConversation,
            options = _botSettings.Options
        });
    }

    [HttpPut("whatsapp")]
    public IActionResult UpdateWhatsApp(BotWhatsAppSettingsDto dto)
    {
        var options = dto.Options?.Select(option => (BotOption)option);
        _botSettings.UpdateWhatsApp(dto.Enabled, dto.Message, dto.MaxAutoReplies, options);

        return Ok(new
        {
            success = true,
            enabled = _botSettings.WhatsAppAutoReplyEnabled,
            message = _botSettings.WhatsAppAutoReplyMessage,
            maxAutoReplies = _botSettings.MaxAutoRepliesPerConversation,
            options = _botSettings.Options
        });
    }

    [HttpPost("whatsapp/reactivar-conversaciones")]
    public async Task<IActionResult> ReactivarConversaciones()
    {
        var actualizadas = await _whatsAppService.ReactivarBotConversacionesPausadasAsync();

        return Ok(new
        {
            success = true,
            actualizadas
        });
    }
}
