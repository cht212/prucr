using System.Security.Claims;
using CRM.Data.Data;
using CRM.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Services;

public sealed class CrmAccessService
{
    private readonly CrmDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CrmPermissionService _permissions;

    public CrmAccessService(CrmDbContext context, IHttpContextAccessor httpContextAccessor, CrmPermissionService permissions)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _permissions = permissions;
    }

    public int? UsuarioActualId =>
        int.TryParse(
            _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : null;

    public string? RolActual =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Role);

    public bool EsAdministrador =>
        _httpContextAccessor.HttpContext?.User.IsInRole(CrmRoles.Administrador) == true;

    public bool EsSupervisor =>
        _httpContextAccessor.HttpContext?.User.IsInRole(CrmRoles.Supervisor) == true;

    public bool EsAuditor =>
        _httpContextAccessor.HttpContext?.User.IsInRole(CrmRoles.Auditor) == true;

    public bool EsAsesor =>
        _httpContextAccessor.HttpContext?.User.IsInRole(CrmRoles.Asesor) == true;

    public bool TieneAccesoGlobal =>
        EsAdministrador || EsSupervisor || EsAuditor;

    public bool PuedeGestionarUsuarios =>
        EsAdministrador;

    public bool PuedeVerDashboard =>
        TieneAccesoGlobal || EsAsesor;

    public async Task<bool> PuedeAccederModuloAsync(string modulo)
    {
        if (string.IsNullOrWhiteSpace(modulo))
        {
            return false;
        }

        return UsuarioActualId.HasValue &&
            await _permissions.HasAsync(UsuarioActualId.Value, RolActual, "modulo." + modulo);
    }

    public static bool EsConversacionDisponibleParaAsesor(Conversacion conversacion) =>
        !conversacion.nUsuarioAsignado.HasValue &&
        conversacion.cEstado is "NUEVO" or "ABIERTO" or "EN_ATENCION";

    public async Task<bool> PuedeVerTodasConversacionesAsync() =>
        UsuarioActualId.HasValue &&
        await _permissions.HasAsync(UsuarioActualId.Value, RolActual, CrmPermissionService.ViewAllChats);

    public IQueryable<Conversacion> FiltrarConversaciones(IQueryable<Conversacion> query, bool? verTodas = null)
    {
        if (verTodas ?? TieneAccesoGlobal)
        {
            return query;
        }

        return UsuarioActualId.HasValue
            ? query.Where(conversacion =>
                conversacion.nUsuarioAsignado == UsuarioActualId.Value ||
                (!conversacion.nUsuarioAsignado.HasValue &&
                 (conversacion.cEstado == "NUEVO" ||
                  conversacion.cEstado == "ABIERTO" ||
                  conversacion.cEstado == "EN_ATENCION")))
            : query.Where(_ => false);
    }

    public IQueryable<Cliente> FiltrarClientes(IQueryable<Cliente> query)
    {
        if (TieneAccesoGlobal)
        {
            return query;
        }

        return UsuarioActualId.HasValue
            ? query.Where(cliente =>
                cliente.Conversaciones.Any(conversacion =>
                    conversacion.nUsuarioAsignado == UsuarioActualId.Value ||
                    (!conversacion.nUsuarioAsignado.HasValue &&
                     (conversacion.cEstado == "NUEVO" ||
                      conversacion.cEstado == "ABIERTO" ||
                      conversacion.cEstado == "EN_ATENCION"))))
            : query.Where(_ => false);
    }

    public IQueryable<Mensaje> FiltrarMensajes(IQueryable<Mensaje> query)
    {
        if (TieneAccesoGlobal)
        {
            return query;
        }

        return UsuarioActualId.HasValue
            ? query.Where(mensaje =>
                mensaje.Conversacion.nUsuarioAsignado == UsuarioActualId.Value)
            : query.Where(_ => false);
    }

    public async Task<IQueryable<Mensaje>> FiltrarMensajesLecturaAsync(IQueryable<Mensaje> query)
    {
        if (!UsuarioActualId.HasValue) return query.Where(_ => false);
        var permisos = await _permissions.GetEffectiveAsync(UsuarioActualId.Value, RolActual);
        if (!permisos.Contains(CrmPermissionService.ModuleInbox) && !permisos.Contains(CrmPermissionService.ModuleLeads))
            return FiltrarMensajes(query);
        var canales = CrmPermissionService.GetAllowedChannels(permisos);
        var visibles = FiltrarConversaciones(_context.Conversaciones.AsNoTracking(), permisos.Contains(CrmPermissionService.ViewAllChats))
            .Where(c => canales.Contains(c.cCanal)).Select(c => c.nConversacion);
        return query.Where(m => visibles.Contains(m.nConversacion));
    }

    public IQueryable<Oportunidad> FiltrarOportunidades(IQueryable<Oportunidad> query)
    {
        if (TieneAccesoGlobal)
        {
            return query;
        }

        return UsuarioActualId.HasValue
            ? query.Where(oportunidad =>
                oportunidad.nUsuarioAsignado == UsuarioActualId.Value ||
                (oportunidad.nConversacion.HasValue &&
                 oportunidad.Conversacion != null &&
                 oportunidad.Conversacion.nUsuarioAsignado == UsuarioActualId.Value) ||
                oportunidad.Cliente.Conversaciones.Any(conversacion =>
                    conversacion.nUsuarioAsignado == UsuarioActualId.Value))
            : query.Where(_ => false);
    }

    public IQueryable<Tarea> FiltrarTareas(IQueryable<Tarea> query)
    {
        if (TieneAccesoGlobal)
        {
            return query;
        }

        return UsuarioActualId.HasValue
            ? query.Where(tarea =>
                tarea.nAsignadoA == UsuarioActualId.Value ||
                (tarea.nConversacion.HasValue &&
                 tarea.Conversacion != null &&
                 tarea.Conversacion.nUsuarioAsignado == UsuarioActualId.Value) ||
                (tarea.nCliente.HasValue &&
                 tarea.Cliente != null &&
                 tarea.Cliente.Conversaciones.Any(conversacion =>
                     conversacion.nUsuarioAsignado == UsuarioActualId.Value)))
            : query.Where(_ => false);
    }

    public async Task<bool> PuedeAccederConversacionAsync(long conversacionId)
    {
        if (!UsuarioActualId.HasValue) return false;
        var permisos = await _permissions.GetEffectiveAsync(UsuarioActualId.Value, RolActual);
        var canales = CrmPermissionService.GetAllowedChannels(permisos);
        // Ver todos los chats no concede atencion sobre chats de otros asesores.
        var puedeGestionarTodos = TieneAccesoGlobal && permisos.Contains(CrmPermissionService.ViewAllChats);
        return await FiltrarConversaciones(_context.Conversaciones.AsNoTracking(), puedeGestionarTodos)
            .Where(conversacion => canales.Contains(conversacion.cCanal))
            .AnyAsync(conversacion => conversacion.nConversacion == conversacionId);
    }

    public async Task<bool> PuedeControlarBotConversacionAsync(long conversacionId)
    {
        if (!await PuedeAccederConversacionAsync(conversacionId)) return false;
        if (TieneAccesoGlobal)
        {
            return await _context.Conversaciones.AsNoTracking()
                .AnyAsync(conversacion => conversacion.nConversacion == conversacionId);
        }

        return UsuarioActualId.HasValue &&
            await _context.Conversaciones.AsNoTracking().AnyAsync(conversacion =>
                conversacion.nConversacion == conversacionId &&
                conversacion.nUsuarioAsignado == UsuarioActualId.Value);
    }

    public async Task<bool> PuedeAccederClienteAsync(long clienteId)
    {
        return await FiltrarClientes(_context.Clientes.AsNoTracking())
            .AnyAsync(cliente => cliente.nCliente == clienteId);
    }

    public async Task<bool> PuedeAccederOportunidadAsync(long oportunidadId)
    {
        return await FiltrarOportunidades(_context.Oportunidades.AsNoTracking())
            .AnyAsync(oportunidad => oportunidad.nOportunidad == oportunidadId);
    }

    public async Task<bool> PuedeAccederTareaAsync(long tareaId)
    {
        return await FiltrarTareas(_context.Tareas.AsNoTracking())
            .AnyAsync(tarea => tarea.nTarea == tareaId);
    }
}
