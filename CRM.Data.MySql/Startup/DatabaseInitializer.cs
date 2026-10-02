using CRM.Data.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Startup;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, ILogger? logger = null)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CrmDbContext>();

        try
        {
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error al validar o migrar la base de datos durante el arranque del CRM.");
            return;
        }

        var bootstrapUsername = configuration["Authentication:BootstrapUsername"];
        var bootstrapPassword = configuration["Authentication:BootstrapPassword"];

        if (string.IsNullOrWhiteSpace(bootstrapUsername) ||
            string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            return;
        }

        if (await context.Usuarios.AnyAsync())
        {
            return;
        }

        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (environment.IsProduction() &&
            bootstrapPassword.Equals("change-me-now", StringComparison.Ordinal))
        {
            const string message =
                "No se puede crear el usuario bootstrap en produccion con la contrasena por defecto.";
            logger?.LogCritical(message);
            throw new InvalidOperationException(message);
        }

        var admin = new CRM.Data.Models.CrmUsuario
        {
            cUsuario = bootstrapUsername.Trim(),
            cNombre = "Administrador",
            cEstado = 'A',
            cRol = "Administrador"
        };

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<CRM.Data.Models.CrmUsuario>>();
        admin.cPasswordHash = hasher.HashPassword(admin, bootstrapPassword);
        context.Usuarios.Add(admin);
        await context.SaveChangesAsync();
    }
}
