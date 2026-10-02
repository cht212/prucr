using CRM.Data.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api")]
public sealed class HealthController : ControllerBase
{
    private readonly CrmDbContext _context;

    public HealthController(CrmDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            sistema = "CRM HPD",
            estado = "OK",
            version = "1.0",
            mensaje = "API funcionando correctamente"
        });
    }

    [HttpGet("health/live")]
    public IActionResult Live()
    {
        return Ok(new
        {
            status = "Healthy",
            service = "CRM HPD",
            timestampUtc = DateTime.UtcNow
        });
    }

    [HttpGet("health/ready")]
    public async Task<IActionResult> Ready()
    {
        var databaseReady = await _context.Database.CanConnectAsync();
        if (!databaseReady)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                status = "Unhealthy",
                database = "Unavailable",
                timestampUtc = DateTime.UtcNow
            });
        }

        return Ok(new
        {
            status = "Healthy",
            database = "Available",
            timestampUtc = DateTime.UtcNow
        });
    }
}
