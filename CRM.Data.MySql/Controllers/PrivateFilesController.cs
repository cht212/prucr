using CRM.Data.Data;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/archivos/local")]
[Authorize]
public sealed class PrivateFilesController : ControllerBase
{
    private readonly CrmDbContext _db;
    private readonly CrmAccessService _access;
    private readonly IWebHostEnvironment _environment;

    public PrivateFilesController(CrmDbContext db, CrmAccessService access, IWebHostEnvironment environment)
    {
        _db = db;
        _access = access;
        _environment = environment;
    }

    [HttpGet("{fileName}")]
    public async Task<IActionResult> Download(string fileName)
    {
        var parts = fileName.Split('.', 2);
        if (parts.Length != 2 || parts[0].Length != 32 ||
            !parts[0].All(Uri.IsHexDigit) ||
            parts[1].Length is < 1 or > 10 || !parts[1].All(char.IsLetterOrDigit))
            return NotFound();

        var url = $"/api/archivos/local/{fileName}";
        if (!await (await _access.FiltrarMensajesLecturaAsync(_db.Mensajes.AsNoTracking()))
                .AnyAsync(m => m.cMensaje.Contains(url)))
            return NotFound();

        var path = Path.Combine(_environment.ContentRootPath, "App_Data", "private-uploads", fileName);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers["Cache-Control"] = "private, no-store";
        return PhysicalFile(path, "application/octet-stream", fileName);
    }
}
