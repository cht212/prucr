using CRM.Data.Data;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/archivos/r2")]
[Authorize]
public sealed class R2FilesController : ControllerBase
{
    private readonly CrmDbContext _db;
    private readonly CrmAccessService _access;
    private readonly R2StorageService _storage;

    public R2FilesController(CrmDbContext db, CrmAccessService access, R2StorageService storage)
    {
        _db = db;
        _access = access;
        _storage = storage;
    }

    [HttpGet]
    public async Task<IActionResult> Download([FromQuery] string key, CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadCore(key, cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    private async Task<IActionResult> DownloadCore(string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 1024)
        {
            return BadRequest("La referencia del archivo es obligatoria.");
        }

        var authorized = await (await _access.FiltrarMensajesLecturaAsync(_db.Mensajes.AsNoTracking()))
            .AnyAsync(message => message.cMensaje.Contains(key), cancellationToken);
        if (!authorized) return NotFound();

        try
        {
            var file = await _storage.DownloadAsync(key, cancellationToken);
            Response.Headers.CacheControl = "private, max-age=300";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(file.Content, file.ContentType);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpGet("/api/archivos/mensajes/{messageId:long}")]
    public async Task<IActionResult> DownloadMessageFile(long messageId, CancellationToken cancellationToken)
    {
        try
        {
            return await DownloadMessageFileCore(messageId, cancellationToken);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested ||
            HttpContext.RequestAborted.IsCancellationRequested)
        {
            return new EmptyResult();
        }
    }

    private async Task<IActionResult> DownloadMessageFileCore(
        long messageId,
        CancellationToken cancellationToken)
    {
        var message = await (await _access.FiltrarMensajesLecturaAsync(_db.Mensajes.AsNoTracking()))
            .Where(item => item.nMensaje == messageId)
            .Select(item => item.cMensaje)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(message)) return NotFound();

        string? objectKey;
        string? fileName;
        string? mimeType;
        try
        {
            using var payload = JsonDocument.Parse(message);
            var root = payload.RootElement;
            objectKey = root.TryGetProperty("publicId", out var keyNode) ? keyNode.GetString() : null;
            fileName = root.TryGetProperty("nombre", out var nameNode)
                ? Path.GetFileName(nameNode.GetString())
                : null;
            mimeType = root.TryGetProperty("mimeType", out var typeNode) ? typeNode.GetString() : null;
        }
        catch (JsonException)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(objectKey)) return NotFound();

        try
        {
            R2DownloadResult file;
            if (objectKey.StartsWith("local/", StringComparison.OrdinalIgnoreCase))
            {
                var storedName = objectKey["local/".Length..];
                file = await _storage.DownloadLocalAsync(storedName, mimeType, cancellationToken);
            }
            else
            {
                file = await _storage.DownloadAsync(objectKey, cancellationToken);
            }

            var contentType = string.IsNullOrWhiteSpace(mimeType) ? file.ContentType : mimeType;
            Response.Headers.CacheControl = "private, max-age=300";
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            if (ShouldDisplayInline(contentType, fileName))
                return File(file.Content, contentType, enableRangeProcessing: true);

            return File(file.Content, contentType, fileName ?? "archivo", enableRangeProcessing: true);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    private static bool ShouldDisplayInline(string contentType, string? fileName) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
        contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ||
        contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);
}
