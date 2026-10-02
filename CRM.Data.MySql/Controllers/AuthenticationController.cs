using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;
using CRM.Data.Data;
using CRM.Data.Models;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController : ControllerBase
{
    // Estado compartido por todas las instancias mediante MySQL.
    private const int MaxIntentos = 5;
    private static readonly TimeSpan TiempoBloqueo = TimeSpan.FromMinutes(15);

    private readonly CrmDbContext _context;
    private readonly IPasswordHasher<CrmUsuario> _hasher;
    private readonly CrmPermissionService _permissions;

    public AuthenticationController(
        CrmDbContext context,
        IPasswordHasher<CrmUsuario> hasher,
        CrmPermissionService permissions)
    {
        _context = context;
        _hasher = hasher;
        _permissions = permissions;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Usuario) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest(new { success = false, message = "Usuario y contraseña son obligatorios." });
        }

        var claveIntento = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(dto.Usuario.Trim().ToUpperInvariant())));
        var estado = await _context.LoginAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Key == claveIntento);
        if (estado?.BlockedUntilUtc > DateTime.UtcNow)
        {
            var minutosRestantes = Math.Ceiling((estado.BlockedUntilUtc.Value - DateTime.UtcNow).TotalMinutes);
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                success = false,
                message = $"Demasiados intentos fallidos. Intenta de nuevo en {minutosRestantes} minuto(s)."
            });
        }

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(item =>
            item.cUsuario == dto.Usuario.Trim() && item.cEstado == 'A');
        if (usuario == null || string.IsNullOrWhiteSpace(usuario.cPasswordHash))
        {
            await RegistrarIntentoFallidoAsync(claveIntento);
            return Unauthorized(new { success = false, message = "Usuario o contraseña incorrectos." });
        }

        var result = _hasher.VerifyHashedPassword(usuario, usuario.cPasswordHash, dto.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            await RegistrarIntentoFallidoAsync(claveIntento);
            return Unauthorized(new { success = false, message = "Usuario o contraseña incorrectos." });
        }

        await _context.LoginAttempts.Where(x => x.Key == claveIntento).ExecuteDeleteAsync();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.nUsuario.ToString()),
            new(ClaimTypes.Name, usuario.cNombre),
            new(ClaimTypes.Role, usuario.cRol)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = dto.Recordarme });

        return Ok(new { success = true, usuario = usuario.cNombre, rol = usuario.cRol });
    }

    private async Task RegistrarIntentoFallidoAsync(string key)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var now = DateTime.UtcNow;

        await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO crm_login_attempt
                (c_key, n_failures, d_blocked_until_utc, d_updated_utc)
            VALUES
                ({key}, 0, NULL, {now})
            ON DUPLICATE KEY UPDATE c_key = c_key
            """);

        var attempt = await _context.LoginAttempts
            .FromSqlInterpolated($"SELECT * FROM crm_login_attempt WHERE c_key = {key} FOR UPDATE")
            .SingleAsync();

        if (attempt.BlockedUntilUtc <= now || now - attempt.UpdatedUtc > TiempoBloqueo)
            attempt.Failures = 0;
        attempt.Failures++;
        attempt.BlockedUntilUtc = attempt.Failures >= MaxIntentos ? now.Add(TiempoBloqueo) : null;
        attempt.UpdatedUtc = now;
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var idValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var role = User.FindFirstValue(ClaimTypes.Role);
        var hasUserId = int.TryParse(idValue, out var userId);
        var permissions = hasUserId
            ? await _permissions.GetEffectiveAsync(userId, role)
            : [];
        var customRoleName = hasUserId
            ? await _context.Usuarios.AsNoTracking()
                .Where(item => item.nUsuario == userId && item.RolPersonalizado != null)
                .Select(item => item.RolPersonalizado!.cNombre)
                .FirstOrDefaultAsync()
            : null;

        return Ok(new
        {
            autenticado = true,
            id = idValue,
            usuario = User.Identity?.Name,
            rol = role,
            rolNombre = customRoleName ?? role,
            permisos = permissions.OrderBy(item => item)
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { success = true });
    }
}

public sealed class LoginDto
{
    public string? Usuario { get; set; }
    public string? Password { get; set; }
    public bool Recordarme { get; set; }
}
