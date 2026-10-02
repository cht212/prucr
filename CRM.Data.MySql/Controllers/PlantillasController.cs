using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/plantillas-rapidas")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public sealed class PlantillasController : ControllerBase
{
    private readonly QuickReplyTemplatesService _templates;

    public PlantillasController(QuickReplyTemplatesService templates)
    {
        _templates = templates;
    }

    [HttpGet]
    public IActionResult Listar()
    {
        return Ok(new
        {
            templates = _templates.GetEnabled()
        });
    }

    [HttpGet("admin")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleBot)]
    public IActionResult ListarAdmin()
    {
        return Ok(new
        {
            templates = _templates.GetAll()
        });
    }

    [HttpPut]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public IActionResult Guardar(QuickReplyTemplatesDto dto)
    {
        var templates = dto.Templates?.Select(template => new QuickReplyTemplate(
            template.Title ?? string.Empty,
            template.Message ?? string.Empty,
            template.Category ?? string.Empty,
            template.Enabled));

        return Ok(new
        {
            success = true,
            templates = _templates.Update(templates)
        });
    }
}

public sealed class QuickReplyTemplatesDto
{
    public List<QuickReplyTemplateDto>? Templates { get; set; }
}

public sealed class QuickReplyTemplateDto
{
    public string? Title { get; set; }
    public string? Message { get; set; }
    public string? Category { get; set; }
    public bool Enabled { get; set; } = true;
}
