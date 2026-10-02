using CRM.Data.Data;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Services;

public sealed record CrmPermissionDefinition(
    string Code,
    string Group,
    string Label,
    string Description);

public sealed class CrmPermissionService
{
    public const string DeniedPrefix = "denegado.";
    public const string ModuleDashboard = "modulo.dashboard";
    public const string ModuleInbox = "modulo.inbox";
    public const string ModuleContacts = "modulo.contactos";
    public const string ModuleTasks = "modulo.tareas";
    public const string ModuleLeads = "modulo.leads";
    public const string ModuleSales = "modulo.ventas";
    public const string ModuleReports = "modulo.reportes";
    public const string ModuleMarketing = "modulo.marketing";
    public const string ModuleBot = "modulo.bot";
    public const string ModuleConnections = "modulo.conexiones";
    public const string ModuleActivity = "modulo.actividad";
    public const string ModuleFailures = "modulo.fallos";
    public const string ModuleUsers = "modulo.usuarios";
    public const string ViewCustomerDetails = "comunicaciones.ficha";
    public const string ViewAllChats = "comunicaciones.chats.todos";
    public const string EditConversationContact = "comunicaciones.ficha.contacto";
    public const string ViewWhatsApp = "comunicaciones.canal.whatsapp";
    public const string ViewInstagram = "comunicaciones.canal.instagram";
    public const string ViewFacebook = "comunicaciones.canal.facebook";
    public const string ViewTikTok = "comunicaciones.canal.tiktok";
    public const string EditContacts = "contactos.editar";
    public const string SendMessages = "mensajes.enviar";
    public const string AssignConversations = "conversaciones.asignar";
    public const string ManageTasks = "tareas.gestionar";
    public const string ManageSales = "ventas.gestionar";
    public const string ManageMarketing = "marketing.gestionar";
    public const string ManageBot = "bot.gestionar";
    public const string ManageIntegrations = "integraciones.gestionar";
    public const string ExportData = "datos.exportar";
    public const string CreateContacts = "contactos.crear";
    public const string AttendConversations = "conversaciones.atender";
    public const string ManageNotes = "clientes.detalles";

    public static readonly IReadOnlyList<CrmPermissionDefinition> Catalog =
    [
        new(ModuleDashboard, "Inicio", "Ver apartado", "Consulta el panel de inicio y sus indicadores."),
        new(ModuleInbox, "Comunicaciones", "Ver apartado", "Consulta las conversaciones disponibles para su cuenta."),
        new(ViewAllChats, "Comunicaciones", "Ver todos los chats", "Consulta los chats de todos los asesores y los cerrados, dentro de los canales permitidos. Sin este permiso solo ve sus chats y los disponibles para tomar."),
        new(ViewCustomerDetails, "Comunicaciones", "Ficha completa del cliente", "Muestra datos, notas, tareas, oportunidades y actividad al abrir una conversación."),
        new(EditConversationContact, "Comunicaciones", "Ficha de datos del contacto", "Permite ver y editar nombre, teléfono, email y documento desde la conversación, sin actividad, tareas ni oportunidades."),
        new(ViewWhatsApp, "Comunicaciones", "Ver WhatsApp", "Muestra las conversaciones del canal WhatsApp."),
        new(ViewInstagram, "Comunicaciones", "Ver Instagram", "Muestra las conversaciones del canal Instagram."),
        new(ViewFacebook, "Comunicaciones", "Ver Facebook", "Muestra las conversaciones del canal Facebook."),
        new(ViewTikTok, "Comunicaciones", "Ver TikTok", "Muestra las conversaciones del canal TikTok."),
        new(SendMessages, "Comunicaciones", "Enviar mensajes y archivos", "Responde conversaciones y adjunta archivos."),
        new(AttendConversations, "Comunicaciones", "Atender conversaciones", "Toma conversaciones y cambia su estado en Comunicaciones y Leads."),
        new(AssignConversations, "Comunicaciones", "Asignar conversaciones", "Asigna o reasigna conversaciones en Comunicaciones y Leads."),
        new(ModuleContacts, "Contactos", "Ver apartado", "Consulta la lista de contactos."),
        new(CreateContacts, "Contactos", "Crear contactos", "Registra nuevos clientes en el CRM."),
        new(EditContacts, "Contactos", "Editar contactos", "Modifica los datos principales de clientes."),
        new(ManageNotes, "Contactos", "Gestionar notas y etiquetas", "Añade notas y etiquetas en las fichas de clientes de Contactos, Comunicaciones y Leads."),
        new(ModuleTasks, "Tareas", "Ver apartado", "Consulta las tareas disponibles para su cuenta."),
        new(ManageTasks, "Tareas", "Gestionar tareas", "Crea, completa y elimina tareas."),
        new(ModuleLeads, "Leads", "Ver apartado", "Consulta leads. Las acciones de atención y asignación se configuran en Comunicaciones."),
        new(ModuleSales, "Ventas", "Ver apartado", "Consulta las oportunidades comerciales disponibles para su cuenta."),
        new(ManageSales, "Ventas", "Gestionar ventas", "Crea y actualiza oportunidades comerciales."),
        new(ModuleReports, "Reportes", "Ver apartado", "Consulta indicadores y reportes del CRM."),
        new(ExportData, "Reportes", "Exportar información", "Permiso compartido para descargar reportes y archivos de datos del CRM."),
        new(ModuleMarketing, "Marketing", "Ver apartado", "Consulta campañas, publicaciones y estadísticas."),
        new(ManageMarketing, "Marketing", "Gestionar marketing", "Crea campañas, ejecuta automatizaciones y responde comentarios."),
        new(ModuleBot, "Bot", "Ver apartado", "Consulta la configuración operativa del bot."),
        new(ManageBot, "Bot", "Configurar bot", "Modifica respuestas y plantillas del bot."),
        new(ModuleConnections, "Conexiones", "Ver apartado", "Consulta el estado de integraciones sin revelar secretos."),
        new(ManageIntegrations, "Conexiones", "Configurar integraciones", "Modifica conexiones externas sin mostrar secretos guardados."),
        new(ModuleActivity, "Actividad", "Ver apartado", "Consulta el historial de auditoría."),
        new(ModuleFailures, "Fallos", "Ver apartado", "Consulta errores de mensajes, webhooks e integraciones."),
        new(ModuleUsers, "Usuarios", "Ver apartado", "Consulta nombres y roles. Crear cuentas, cambiar claves y administrar permisos es exclusivo del Administrador."),
    ];

    private static readonly HashSet<string> ConfigurableCodes =
        Catalog.Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private readonly CrmDbContext _context;
    private readonly Dictionary<(int UserId, string Role), HashSet<string>> _effectiveCache = new();

    public CrmPermissionService(CrmDbContext context)
    {
        _context = context;
    }

    public static bool IsConfigurable(string permission) =>
        ConfigurableCodes.Contains(permission);

    public async Task<HashSet<string>> GetGrantedAsync(int userId) =>
        (await _context.UsuarioPermisos
            .AsNoTracking()
            .Where(item => item.nUsuario == userId)
            .Select(item => item.cPermiso)
            .ToListAsync())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<HashSet<string>> GetEffectiveAsync(int userId, string? role)
    {
        var key = (userId, role ?? "");
        if (_effectiveCache.TryGetValue(key, out var cached)) return cached;
        if (CrmRoles.Normalize(role).Equals(CrmRoles.Administrador, StringComparison.OrdinalIgnoreCase))
            return GetBasePermissions(role);
        var inherited = await GetInheritedPermissionsAsync(userId, role);
        var effective = ApplyOverrides(inherited, await GetGrantedAsync(userId));
        _effectiveCache[key] = effective;
        return effective;
    }

    public async Task<HashSet<string>> GetInheritedPermissionsAsync(int userId, string? role)
    {
        var assignedRole = await _context.Usuarios
            .AsNoTracking()
            .Where(item => item.nUsuario == userId && item.nRol != null && item.RolPersonalizado!.cEstado == 'A')
            .Select(item => new
            {
                item.RolPersonalizado!.cRolBase,
                Permisos = item.RolPersonalizado.Permisos.Select(permission => permission.cPermiso).ToList()
            })
            .FirstOrDefaultAsync();

        return assignedRole == null
            ? GetBasePermissions(role)
            : ResolveEffective(assignedRole.cRolBase, assignedRole.Permisos);
    }

    // Conserva los permisos adicionales existentes y almacena las excepciones
    // negativas en la misma tabla, sin alterar el esquema ni los roles.
    public static HashSet<string> ResolveEffective(string? role, IEnumerable<string> overrides)
    {
        if (CrmRoles.Normalize(role).Equals(CrmRoles.Administrador, StringComparison.OrdinalIgnoreCase))
            return GetBasePermissions(role);
        return ApplyOverrides(GetBasePermissions(role), overrides);
    }

    public static HashSet<string> ApplyOverrides(IEnumerable<string> inheritedPermissions, IEnumerable<string> overrides)
    {
        var result = inheritedPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stored = overrides.ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.UnionWith(stored.Where(IsConfigurable));
        result.ExceptWith(stored.Where(item => item.StartsWith(DeniedPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(item => item[DeniedPrefix.Length..]));
        foreach (var group in Catalog.GroupBy(item => item.Group, StringComparer.OrdinalIgnoreCase))
        {
            var module = group.FirstOrDefault(item => item.Code.StartsWith("modulo.", StringComparison.OrdinalIgnoreCase));
            if (module != null && !result.Contains(module.Code))
                result.ExceptWith(group.Select(item => item.Code));
        }
        return result;
    }

    public static HashSet<string> BuildOverrides(string? role, IEnumerable<string> selected)
    {
        return BuildOverrides(GetBasePermissions(role), selected);
    }

    public static HashSet<string> BuildOverrides(IEnumerable<string> inheritedPermissions, IEnumerable<string> selected)
    {
        var requested = selected.Where(IsConfigurable).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Catalog.GroupBy(item => item.Group, StringComparer.OrdinalIgnoreCase))
        {
            var module = group.FirstOrDefault(item => item.Code.StartsWith("modulo.", StringComparison.OrdinalIgnoreCase));
            if (module != null && !requested.Contains(module.Code))
                requested.ExceptWith(group.Select(item => item.Code));
        }
        var inherited = inheritedPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = requested.Except(inherited, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.UnionWith(inherited.Except(requested, StringComparer.OrdinalIgnoreCase).Select(item => DeniedPrefix + item));
        return result;
    }

    public async Task<bool> HasAsync(int userId, string? role, string permission)
    {
        if (CrmRoles.Normalize(role).Equals(CrmRoles.Administrador, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (await GetEffectiveAsync(userId, role)).Contains(permission);
    }

    public static HashSet<string> GetBasePermissions(string? role)
    {
        var result = GetBaseActionPermissions(role);
        result.UnionWith(Catalog.Where(item => item.Code.StartsWith("modulo.") &&
            CrmRolePermissions.CanAccessModule(role, item.Code["modulo.".Length..])).Select(item => item.Code));
        if (result.Contains(ModuleInbox))
        {
            result.UnionWith([ViewCustomerDetails, ViewWhatsApp, ViewInstagram, ViewFacebook, ViewTikTok]);
        }
        return result;
    }

    public static string? GetChannelPermission(string? channel) =>
        (channel ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "WHATSAPP" => ViewWhatsApp,
            "INSTAGRAM" => ViewInstagram,
            "FACEBOOK" => ViewFacebook,
            "TIKTOK" => ViewTikTok,
            _ => null
        };

    public static string[] GetAllowedChannels(IEnumerable<string> effectivePermissions)
    {
        var permissions = effectivePermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new[] { "WHATSAPP", "INSTAGRAM", "FACEBOOK", "TIKTOK" }
            .Where(channel => permissions.Contains(GetChannelPermission(channel)!))
            .ToArray();
    }

    private static HashSet<string> GetBaseActionPermissions(string? role)
    {
        var normalized = CrmRoles.Normalize(role);
        if (normalized.Equals(CrmRoles.Administrador, StringComparison.OrdinalIgnoreCase))
        {
            return Catalog.Select(item => item.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (normalized.Equals(CrmRoles.Supervisor, StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ModuleReports, ModuleMarketing, ModuleBot, ModuleActivity,
                ViewAllChats, EditContacts, SendMessages, AssignConversations, ManageTasks,
                ManageSales, ManageMarketing, ManageBot, ManageIntegrations,
                ExportData, CreateContacts, AttendConversations, ManageNotes
            };
        }

        if (normalized.Equals(CrmRoles.Asesor, StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EditContacts, SendMessages, ManageTasks, ManageSales, CreateContacts,
                AttendConversations, ManageNotes
            };
        }

        if (normalized.Equals(CrmRoles.Auditor, StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ModuleReports, ModuleMarketing, ModuleBot, ModuleConnections,
                ModuleActivity, ModuleFailures, ModuleUsers, ExportData, ViewAllChats
            };
        }

        if (normalized.Equals(CrmRoles.Marketing, StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ModuleMarketing,
                ManageMarketing
            };
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public static string? ResolveMutationPermission(PathString path)
    {
        var value = path.Value?.ToLowerInvariant() ?? string.Empty;
        // Los endpoints de autenticación no son operaciones del CRM. En
        // particular, login debe funcionar aunque el navegador conserve una
        // cookie de una sesión anterior con permisos limitados.
        if (value.StartsWith("/api/auth") || value.Contains("/webhook")) return null;
        if (value.StartsWith("/api/crm/usuarios") || value.StartsWith("/api/crm/roles")) return ModuleUsers;
        if (value.Contains("/asignar") || value.Contains("/asignar-pendientes")) return AssignConversations;
        if (value.StartsWith("/api/whatsapp"))
            return value.EndsWith("/bot") ? AttendConversations : SendMessages;
        if (value.StartsWith("/api/tareas")) return ManageTasks;
        if (value.StartsWith("/api/oportunidades")) return ManageSales;
        if (value.StartsWith("/api/crm/comentarios")) return ManageMarketing;
        if (value.StartsWith("/api/campanas") || value.StartsWith("/api/automatizacion")) return ManageMarketing;
        if (value.StartsWith("/api/bot") || value.StartsWith("/api/plantillas")) return ManageBot;
        if (value.StartsWith("/api/integraciones")) return ManageIntegrations;
        if (value.StartsWith("/api/exportaciones")) return ExportData;
        if (value.StartsWith("/api/notas") || value.StartsWith("/api/clientes") || value.StartsWith("/api/etiquetas")) return ManageNotes;
        if (value.StartsWith("/api/crm/contactos"))
        {
            if (value.Contains("/conversaciones")) return AttendConversations;
            return value.Count(character => character == '/') <= 3 ? CreateContacts : EditContacts;
        }
        if (value.StartsWith("/api/crm/conversaciones")) return AttendConversations;
        return "operacion.no_asignada";
    }

    public static bool IsContactUpdatePath(PathString path) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            (path.Value ?? string.Empty).TrimEnd('/'),
            @"^/api/crm/contactos/\d+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static string[] ResolveReadPermissions(PathString path)
    {
        var value = (path.Value?.ToLowerInvariant() ?? "").TrimEnd('/');
        if (value.EndsWith("/exportar")) return [ExportData];
        if (value.StartsWith("/api/crm/reportes")) return [ModuleReports, ModuleDashboard];
        if (value.StartsWith("/api/dashboard")) return [ModuleDashboard];
        if (value.StartsWith("/api/crm/leads")) return [ModuleLeads];
        if (value.StartsWith("/api/crm/fallos")) return [ModuleFailures];
        if (value == "/api/crm/actividad") return [ModuleActivity];
        if (value.StartsWith("/api/crm/actividad/historial")) return [ModuleActivity, ModuleContacts, ModuleInbox, ModuleLeads];
        if (value.StartsWith("/api/crm/comentarios") || value.StartsWith("/api/campanas") || value.StartsWith("/api/automatizacion"))
            return [ModuleMarketing];
        if (value.StartsWith("/api/bot") || value == "/api/plantillas-rapidas/admin") return [ModuleBot];
        if (value == "/api/plantillas-rapidas") return [ModuleBot, ModuleInbox, ModuleLeads];
        if (value.StartsWith("/api/tareas")) return [ModuleTasks, ModuleInbox, ModuleLeads, ModuleContacts];
        if (value.StartsWith("/api/oportunidades")) return [ModuleSales, ModuleLeads, ModuleInbox, ModuleContacts];
        if (value.StartsWith("/api/exportaciones")) return [ExportData];
        // Los selectores y fichas utilizan datos compartidos entre apartados.
        // Permitirlos cuando existe un apartado que los necesita.
        if (value == "/api/crm/usuarios") return [ModuleUsers, ModuleDashboard, ModuleInbox, ModuleContacts, ModuleTasks, ModuleLeads, ModuleSales, ModuleReports];
        if (value.StartsWith("/api/crm/roles")) return [ModuleUsers];
        if (value.StartsWith("/api/crm/contactos") || value.StartsWith("/api/clientes") || value.StartsWith("/api/etiquetas"))
            return [ModuleContacts, ModuleInbox, ModuleLeads, ModuleSales, ModuleTasks];
        if (value.StartsWith("/api/whatsapp/conversaciones")) return [ModuleInbox, ModuleLeads, ModuleContacts];
        return [];
    }
}
