using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using CRM.Data.Data;
using CRM.Data.Models;
using CRM.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Controllers;

[ApiController]
[Route("api/crm")]
[Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
[EnableRateLimiting("api")]
public class CrmManagementController : ControllerBase
{
    private static readonly string[] EstadosConversacionPermitidos =
    {
        "NUEVO",
        "ABIERTO",
        "EN_ATENCION",
        "ESPERANDO_CLIENTE",
        "COTIZACION_ENVIADA",
        "CERRADO",
        "PERDIDO",
        "NO_RESPONDIO"
    };

    private readonly CrmDbContext _context;
    private readonly IPasswordHasher<CrmUsuario> _hasher;
    private readonly WhatsAppService _whatsappService;
    private readonly AuditoriaService _auditoria;
    private readonly MetaGraphApiService _metaGraph;
    private readonly CrmAccessService _access;
    private readonly CrmPermissionService _permissions;

    public CrmManagementController(
        CrmDbContext context,
        IPasswordHasher<CrmUsuario> hasher,
        WhatsAppService whatsappService,
        AuditoriaService auditoria,
        MetaGraphApiService metaGraph,
        CrmAccessService access,
        CrmPermissionService permissions)
    {
        _context = context;
        _hasher = hasher;
        _whatsappService = whatsappService;
        _auditoria = auditoria;
        _metaGraph = metaGraph;
        _access = access;
        _permissions = permissions;
    }

    private int? UsuarioActualId =>
        int.TryParse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet("contactos")]
    public async Task<IActionResult> Contactos(
        [FromQuery] string? search = null,
        [FromQuery] string? canal = null,
        [FromQuery] int? usuarioId = null,
        [FromQuery] int? etiquetaId = null,
        [FromQuery] int? page = null,
        [FromQuery] int pageSize = 50)
    {
        var puedeVerFicha = UsuarioActualId.HasValue &&
            await _permissions.HasAsync(UsuarioActualId.Value, _access.RolActual, CrmPermissionService.ViewCustomerDetails);
        if (etiquetaId.HasValue && !puedeVerFicha) return Forbid();
        pageSize = Math.Clamp(pageSize, 1, 200);
        var numeroPagina = Math.Max(1, page ?? 1);
        // En Contactos, cada asesor ve solamente clientes que tengan al menos
        // una conversación asignada a él. Los chats sin asignar siguen siendo
        // accesibles desde Comunicaciones para poder completar su ficha.
        var clientes = _context.Clientes.AsNoTracking();
        var query = _access.EsAsesor
            ? UsuarioActualId.HasValue
                ? clientes.Where(cliente => cliente.Conversaciones.Any(conversacion =>
                    conversacion.nUsuarioAsignado == UsuarioActualId.Value))
                : clientes.Where(_ => false)
            : _access.FiltrarClientes(clientes);
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(cliente =>
                cliente.cNombre.Contains(search) ||
                cliente.cTelefono.Contains(search) ||
                (cliente.cEmail != null && cliente.cEmail.Contains(search)) ||
                (cliente.cDocumento != null && cliente.cDocumento.Contains(search)) ||
                cliente.Conversaciones.Any(conversacion =>
                    conversacion.cCanal.Contains(search) ||
                    (conversacion.UsuarioAsignado != null && conversacion.UsuarioAsignado.cNombre.Contains(search))) ||
                (puedeVerFicha && _context.ClienteEtiquetas.Any(clienteEtiqueta =>
                    clienteEtiqueta.nCliente == cliente.nCliente &&
                    clienteEtiqueta.Etiqueta.cNombre.Contains(search))));
        }

        if (!string.IsNullOrWhiteSpace(canal) && !canal.Equals("TODOS", StringComparison.OrdinalIgnoreCase))
        {
            var canalNormalizado = CanalSocial.Normalizar(canal);
            query = query.Where(cliente =>
                cliente.cCanalOrigen == canalNormalizado ||
                cliente.Conversaciones.Any(conversacion => conversacion.cCanal == canalNormalizado));
        }

        if (usuarioId.HasValue)
        {
            if (_access.TieneAccesoGlobal)
            {
                query = query.Where(cliente =>
                    cliente.Conversaciones.Any(conversacion => conversacion.nUsuarioAsignado == usuarioId.Value));
            }
        }

        if (etiquetaId.HasValue)
        {
            query = query.Where(cliente =>
                _context.ClienteEtiquetas.Any(clienteEtiqueta =>
                    clienteEtiqueta.nCliente == cliente.nCliente &&
                    clienteEtiqueta.nEtiqueta == etiquetaId.Value));
        }

        var total = page.HasValue ? await query.CountAsync() : 0;
        if (page.HasValue) numeroPagina = Math.Min(numeroPagina, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var result = await query
            .OrderByDescending(cliente => cliente.dFechaRegistro)
            .ThenByDescending(cliente => cliente.nCliente)
            .Skip(page.HasValue ? (numeroPagina - 1) * pageSize : 0)
            .Take(page.HasValue ? pageSize : 200)
            .Select(cliente => new
            {
                id = cliente.nCliente,
                nombre = cliente.cNombre,
                telefono = cliente.cTelefono,
                email = cliente.cEmail,
                documento = cliente.cDocumento,
                fotoPerfilUrl = cliente.cFotoPerfilUrl,
                estado = cliente.cEstado,
                canalOrigen = cliente.cCanalOrigen,
                etiquetas = _context.ClienteEtiquetas
                    .Where(clienteEtiqueta => puedeVerFicha && clienteEtiqueta.nCliente == cliente.nCliente)
                    .Select(clienteEtiqueta => new
                    {
                        id = clienteEtiqueta.nEtiqueta,
                        nombre = clienteEtiqueta.Etiqueta.cNombre,
                        color = clienteEtiqueta.Etiqueta.cColor
                    })
                    .ToList(),
                conversaciones = cliente.Conversaciones.Count,
                ultimaConversacionId = cliente.Conversaciones
                    .OrderByDescending(conversacion => conversacion.dUltimoMensaje)
                    .Select(conversacion => (long?)conversacion.nConversacion)
                    .FirstOrDefault(),
                ultimoMensaje = cliente.Conversaciones
                    .OrderByDescending(conversacion => conversacion.dUltimoMensaje)
                    .Select(conversacion => conversacion.dUltimoMensaje)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return page.HasValue ? Ok(new { total, page = numeroPagina, pageSize, items = result }) : Ok(result);
    }
    [HttpPost("contactos")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> CrearContacto([FromBody] ContactoDto dto)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var nombre = dto.Nombre?.Trim();
        var telefono = dto.Telefono?.Trim();
        if (string.IsNullOrWhiteSpace(nombre) || string.IsNullOrWhiteSpace(telefono))
        {
            return BadRequest("Nombre y teléfono son obligatorios.");
        }

        if (await _context.Clientes.AnyAsync(cliente => cliente.cTelefono == telefono))
        {
            return Conflict("Ya existe un contacto con ese teléfono.");
        }

        var clienteNuevo = new Cliente
        {
            cNombre = nombre,
            cTelefono = telefono,
            cEmail = dto.Email,
            cDocumento = dto.Documento,
            cCanalOrigen = CanalSocial.Normalizar(dto.CanalOrigen),
            dFechaRegistro = DateTime.Now,
            cEstado = 'A'
        };

        _context.Clientes.Add(clienteNuevo);
        await _context.SaveChangesAsync();

        long? conversacionInicialId = null;
        if (_access.EsAsesor && UsuarioActualId.HasValue)
        {
            var conversacionInicial = new Conversacion
            {
                nCliente = clienteNuevo.nCliente,
                nUsuarioAsignado = UsuarioActualId.Value,
                cEstado = "EN_ATENCION",
                cCanal = clienteNuevo.cCanalOrigen,
                cExternalThreadId = clienteNuevo.cTelefono,
                cBotEstado = "PAUSADO",
                dFechaInicio = DateTime.Now,
                dUltimoMensaje = DateTime.Now,
                dBotPausadoDesde = DateTime.Now,
                nBotPausadoPor = UsuarioActualId.Value
            };

            _context.Conversaciones.Add(conversacionInicial);
            await _context.SaveChangesAsync();
            conversacionInicialId = conversacionInicial.nConversacion;
        }

        await _auditoria.RegistrarAsync("Cliente", clienteNuevo.nCliente, "CREACION", null,
            $"{clienteNuevo.cNombre} / {clienteNuevo.cTelefono}", UsuarioActualId);

        return Ok(new
        {
            success = true,
            id = clienteNuevo.nCliente,
            nombre = clienteNuevo.cNombre,
            telefono = clienteNuevo.cTelefono,
            conversacionId = conversacionInicialId
        });
    }

    [HttpPost("contactos/{id:long}/conversaciones")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> CrearConversacionContacto(long id)
    {
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente == null)
        {
            return NotFound("Cliente no encontrado.");
        }

        if (!_access.TieneAccesoGlobal && !await _access.PuedeAccederClienteAsync(id))
        {
            return Forbid();
        }

        var conversacionAbierta = await _context.Conversaciones
            .Where(conversacion =>
                conversacion.nCliente == id &&
                (conversacion.cEstado == "NUEVO" ||
                 conversacion.cEstado == "ABIERTO" ||
                 conversacion.cEstado == "EN_ATENCION"))
            .OrderByDescending(conversacion => conversacion.dUltimoMensaje)
            .FirstOrDefaultAsync();

        if (conversacionAbierta != null)
        {
            return Ok(new { success = true, id = conversacionAbierta.nConversacion, existente = true });
        }

        var nuevaConversacion = new Conversacion
        {
            nCliente = id,
            nUsuarioAsignado = UsuarioActualId,
            cEstado = UsuarioActualId.HasValue ? "EN_ATENCION" : "NUEVO",
            cCanal = cliente.cCanalOrigen,
            cExternalThreadId = cliente.cTelefono,
            cBotEstado = "PAUSADO",
            dFechaInicio = DateTime.Now,
            dUltimoMensaje = DateTime.Now,
            dBotPausadoDesde = DateTime.Now,
            nBotPausadoPor = UsuarioActualId
        };

        _context.Conversaciones.Add(nuevaConversacion);
        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync("Conversacion", nuevaConversacion.nConversacion, "CREACION_MANUAL", null,
            $"Cliente {cliente.cNombre}", UsuarioActualId);

        return Ok(new { success = true, id = nuevaConversacion.nConversacion, existente = false });
    }

    // Antes traía TODAS las conversaciones sin límite: con volumen real
    // esto se vuelve lento y pesado para el navegador. Ahora pagina y
    // permite filtrar por estado.
    [HttpGet("leads")]
    public async Task<IActionResult> Leads(
        [FromQuery] string? estado = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var permisos = await _permissions.GetEffectiveAsync(UsuarioActualId ?? 0, _access.RolActual);
        var canales = CrmPermissionService.GetAllowedChannels(permisos);
        var query = _access
            .FiltrarConversaciones(_context.Conversaciones.AsNoTracking(), await _access.PuedeVerTodasConversacionesAsync())
            .Where(c => canales.Contains(c.cCanal))
            .Where(conversacion => conversacion.Mensajes.Any(mensaje =>
                mensaje.cTipo != "comment" &&
                mensaje.cTipo != "comment_reply"));
        var porEstado = await query.GroupBy(c => c.cEstado)
            .Select(g => new { estado = g.Key, cantidad = g.Count() }).ToListAsync();
        if (!string.IsNullOrWhiteSpace(estado))
        {
            query = query.Where(c => c.cEstado == estado.Trim().ToUpperInvariant());
        }

        var total = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));

        var items = await query
            .Include(conversacion => conversacion.Cliente)
            .Include(conversacion => conversacion.UsuarioAsignado)
            .OrderByDescending(conversacion => conversacion.dUltimoMensaje)
            .ThenByDescending(conversacion => conversacion.nConversacion)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(conversacion => new
            {
                id = conversacion.nConversacion,
                estado = conversacion.cEstado,
                canal = conversacion.cCanal,
                cliente = new
                {
                    id = conversacion.Cliente.nCliente,
                    nombre = conversacion.Cliente.cNombre,
                    telefono = conversacion.Cliente.cTelefono,
                    fotoPerfilUrl = conversacion.Cliente.cFotoPerfilUrl
                },
                asesor = conversacion.UsuarioAsignado == null
                    ? null
                    : conversacion.UsuarioAsignado.cNombre,
                proximaTarea = _context.Tareas
                    .Where(tarea =>
                        tarea.nConversacion == conversacion.nConversacion &&
                        tarea.cEstado == "PENDIENTE")
                    .OrderBy(tarea => tarea.dFechaVencimiento)
                    .Select(tarea => new
                    {
                        id = tarea.nTarea,
                        titulo = tarea.cTitulo,
                        vence = tarea.dFechaVencimiento,
                        vencida = tarea.dFechaVencimiento < DateTime.Now
                    })
                    .FirstOrDefault(),
                ultimoMensaje = conversacion.dUltimoMensaje,
                inicio = conversacion.dFechaInicio
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items, porEstado });
    }

    [HttpGet("usuarios")]
    public async Task<IActionResult> Usuarios([FromQuery] int? page = null, [FromQuery] int pageSize = 10, [FromQuery] string? search = null)
    {
        var canViewUsers = UsuarioActualId.HasValue &&
            await _permissions.HasAsync(UsuarioActualId.Value, _access.RolActual, CrmPermissionService.ModuleUsers);

        var query = _context.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.cEstado == 'A');

        if (!_access.TieneAccesoGlobal && !canViewUsers && !await _access.PuedeVerTodasConversacionesAsync())
        {
            query = query.Where(usuario => usuario.nUsuario == UsuarioActualId!.Value);
        }

        var resumen = page.HasValue ? await query.GroupBy(_ => 1).Select(g => new
        {
            activos = g.Count(), configurables = g.Count(u => u.cRol != "Administrador")
        }).FirstOrDefaultAsync() : null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var termino = search.Trim();
            query = query.Where(u => u.cNombre.Contains(termino) || u.cUsuario.Contains(termino) ||
                u.cRol.Contains(termino) || (u.RolPersonalizado != null && u.RolPersonalizado.cNombre.Contains(termino)));
        }
        var total = page.HasValue ? await query.CountAsync() : 0;
        pageSize = Math.Clamp(pageSize, 1, 100);
        var numeroPagina = Math.Max(1, page ?? 1);
        if (page.HasValue) numeroPagina = Math.Min(numeroPagina, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var listado = query.OrderBy(u => u.cNombre).ThenBy(u => u.nUsuario).AsQueryable();
        if (page.HasValue) listado = listado.Skip((numeroPagina - 1) * pageSize).Take(pageSize);
        var result = await listado
            .Select(usuario => new
            {
                id = usuario.nUsuario,
                usuario = usuario.cUsuario,
                nombre = usuario.cNombre,
                rol = usuario.RolPersonalizado != null ? usuario.RolPersonalizado.cNombre : usuario.cRol,
                rolBase = usuario.cRol,
                rolId = usuario.nRol
            })
            .ToListAsync();

        return page.HasValue ? Ok(new { total, page = numeroPagina, pageSize, items = result, resumen }) : Ok(result);
    }

    [HttpPost("usuarios")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> CrearUsuario([FromBody] CrearUsuarioDto dto)
    {
        var usuario = (dto.Usuario ?? string.Empty).Trim();
        var nombre = (dto.Nombre ?? string.Empty).Trim();
        var rol = CrmRoles.Normalize(dto.Rol);

        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(nombre) ||
            string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest("Usuario, nombre y contraseña son obligatorios.");
        }

        if (dto.Password.Length < 8)
        {
            return BadRequest("La contraseña debe tener al menos 8 caracteres.");
        }

        CrmRol? rolPersonalizado = null;
        if (dto.RolId.HasValue)
        {
            rolPersonalizado = await _context.Roles.FirstOrDefaultAsync(item =>
                item.nRol == dto.RolId.Value && item.cEstado == 'A');
            if (rolPersonalizado == null) return BadRequest("El rol seleccionado no existe o está inactivo.");
            rol = rolPersonalizado.cRolBase;
        }

        if (!new[] { CrmRoles.Auditor, CrmRoles.Supervisor, CrmRoles.Asesor, CrmRoles.Marketing }
            .Contains(rol, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest("El rol debe ser Auditor, Supervisor, Asesor o Marketing. La cuenta Administrador es única.");
        }

        if (await _context.Usuarios.AnyAsync(item => item.cUsuario == usuario))
        {
            return Conflict("El usuario ya existe.");
        }

        var nuevoUsuario = new CrmUsuario
        {
            cUsuario = usuario,
            cNombre = nombre,
            cEstado = 'A',
            cRol = rol,
            nRol = rolPersonalizado?.nRol
        };
        nuevoUsuario.cPasswordHash = _hasher.HashPassword(nuevoUsuario, dto.Password);
        _context.Usuarios.Add(nuevoUsuario);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            success = true,
            id = nuevoUsuario.nUsuario,
            usuario = nuevoUsuario.cUsuario,
            nombre = nuevoUsuario.cNombre,
            rol = rolPersonalizado?.cNombre ?? nuevoUsuario.cRol,
            rolBase = nuevoUsuario.cRol,
            rolId = nuevoUsuario.nRol
        });
    }

    [HttpGet("roles")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> Roles()
    {
        var roles = await _context.Roles
            .AsNoTracking()
            .Where(item => item.cEstado == 'A')
            .OrderBy(item => item.cNombre)
            .Select(item => new
            {
                id = item.nRol,
                nombre = item.cNombre,
                descripcion = item.cDescripcion,
                rolBase = item.cRolBase,
                usuarios = item.Usuarios.Count(usuario => usuario.cEstado == 'A'),
                permisosGuardados = item.Permisos.Select(permission => permission.cPermiso).ToList()
            })
            .ToListAsync();

        return Ok(new
        {
            bases = new[] { CrmRoles.Asesor, CrmRoles.Supervisor, CrmRoles.Marketing, CrmRoles.Auditor }
                .Select(nombre => new { nombre, permisos = CrmPermissionService.GetBasePermissions(nombre).OrderBy(item => item) }),
            catalogo = CrmPermissionService.Catalog.Select(item => new
            {
                codigo = item.Code,
                grupo = item.Group,
                nombre = item.Label,
                descripcion = item.Description
            }),
            roles = roles.Select(item => new
            {
                item.id,
                item.nombre,
                item.descripcion,
                item.rolBase,
                item.usuarios,
                permisos = CrmPermissionService.ResolveEffective(item.rolBase, item.permisosGuardados)
                    .OrderBy(permission => permission)
            })
        });
    }

    [HttpPost("roles")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> CrearRol([FromBody] GuardarRolDto dto)
    {
        return await GuardarRolAsync(null, dto);
    }

    [HttpPut("roles/{id:int}")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> EditarRol(int id, [FromBody] GuardarRolDto dto)
    {
        return await GuardarRolAsync(id, dto);
    }

    [HttpDelete("roles/{id:int}")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> EliminarRol(int id)
    {
        var role = await _context.Roles.FirstOrDefaultAsync(item => item.nRol == id && item.cEstado == 'A');
        if (role == null) return NotFound("Rol no encontrado.");

        var assignedUsers = await _context.Usuarios.CountAsync(item => item.nRol == id);
        if (assignedUsers > 0)
            return Conflict("No puedes eliminar este rol porque tiene usuarios asignados. Reasígnalos primero.");

        var previous = role.cNombre;
        _context.Roles.Remove(role);
        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync("Rol", id, "ELIMINACION", previous, null, UsuarioActualId);
        return Ok(new { success = true });
    }

    private async Task<IActionResult> GuardarRolAsync(int? id, GuardarRolDto dto)
    {
        var nombre = (dto.Nombre ?? string.Empty).Trim();
        var descripcion = (dto.Descripcion ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nombre)) return BadRequest("El nombre del rol es obligatorio.");
        if (nombre.Length > 80 || descripcion.Length > 250) return BadRequest("El nombre o la descripción exceden el tamaño permitido.");
        if (dto.Permisos == null || dto.Permisos.Any(item => !CrmPermissionService.IsConfigurable(item)))
            return BadRequest("La lista contiene permisos no válidos.");
        if (await _context.Roles.AnyAsync(item => item.cNombre == nombre && (!id.HasValue || item.nRol != id.Value)))
            return Conflict("Ya existe un rol con ese nombre.");

        CrmRol role;
        string? previous = null;
        if (id.HasValue)
        {
            var existingRole = await _context.Roles.Include(item => item.Permisos)
                .FirstOrDefaultAsync(item => item.nRol == id.Value && item.cEstado == 'A');
            if (existingRole == null) return NotFound("Rol no encontrado.");
            role = existingRole;
            previous = role.cNombre;
        }
        else
        {
            role = new CrmRol
            {
                cRolBase = CrmRoles.Asesor,
                nCreadoPor = UsuarioActualId,
                dFechaCreacion = DateTime.UtcNow
            };
            _context.Roles.Add(role);
        }

        role.cNombre = nombre;
        role.cDescripcion = descripcion;
        var stored = CrmPermissionService.BuildOverrides(role.cRolBase, dto.Permisos);
        if (id.HasValue)
        {
            var currentPermissions = role.Permisos.Select(item => item.cPermiso)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            _context.RolPermisos.RemoveRange(role.Permisos.Where(item => !stored.Contains(item.cPermiso)).ToList());
            foreach (var permission in stored.Where(item => !currentPermissions.Contains(item)))
                role.Permisos.Add(new CrmRolPermiso { cPermiso = permission });
        }
        else
        {
            role.Permisos = stored.Select(permission => new CrmRolPermiso { cPermiso = permission }).ToList();
        }
        await _context.SaveChangesAsync();
        if (id.HasValue)
        {
            await _context.Usuarios.Where(item => item.nRol == role.nRol)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.cRol, role.cRolBase));
        }
        await _auditoria.RegistrarAsync("Rol", role.nRol, id.HasValue ? "EDICION" : "CREACION", previous,
            role.cNombre, UsuarioActualId);

        return Ok(new { success = true, id = role.nRol, nombre = role.cNombre });
    }

    [HttpPut("usuarios/{id:int}/password")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> CambiarPassword(int id, [FromBody] CambiarPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 8)
        {
            return BadRequest("La contraseña debe tener al menos 8 caracteres.");
        }

        var usuario = await _context.Usuarios.FindAsync(id);
        if (usuario == null)
        {
            return NotFound("Usuario no encontrado.");
        }

        usuario.cPasswordHash = _hasher.HashPassword(usuario, dto.Password);
        await _context.SaveChangesAsync();
        return Ok(new { success = true });
    }

    [HttpPut("usuarios/{id:int}")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> EditarUsuario(int id, [FromBody] EditarUsuarioDto dto)
    {
        var nombre = (dto.Nombre ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nombre)) return BadRequest("El nombre es obligatorio.");
        if (!string.IsNullOrEmpty(dto.Password) && dto.Password.Length < 8)
            return BadRequest("La contraseña debe tener al menos 8 caracteres.");

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(item => item.nUsuario == id);
        if (usuario == null) return NotFound("Usuario no encontrado.");

        var rolAnterior = usuario.nRol;
        var rolBaseAnterior = usuario.cRol;
        if (!CrmRoles.Normalize(usuario.cRol).Equals(CrmRoles.Administrador, StringComparison.OrdinalIgnoreCase))
        {
            if (dto.RolId.HasValue)
            {
                var rol = await _context.Roles.FirstOrDefaultAsync(item => item.nRol == dto.RolId.Value && item.cEstado == 'A');
                if (rol == null) return BadRequest("El rol seleccionado no existe o está inactivo.");
                usuario.nRol = rol.nRol;
                usuario.cRol = rol.cRolBase;
            }
            else if (!string.IsNullOrWhiteSpace(dto.Rol))
            {
                var rol = CrmRoles.Normalize(dto.Rol);
                if (!new[] { CrmRoles.Auditor, CrmRoles.Supervisor, CrmRoles.Asesor, CrmRoles.Marketing }
                    .Contains(rol, StringComparer.OrdinalIgnoreCase))
                    return BadRequest("Selecciona un rol válido.");
                usuario.nRol = null;
                usuario.cRol = rol;
            }
        }

        var anterior = usuario.cNombre;
        usuario.cNombre = nombre;
        if (!string.IsNullOrWhiteSpace(dto.Password))
            usuario.cPasswordHash = _hasher.HashPassword(usuario, dto.Password);

        if (rolAnterior != usuario.nRol || !rolBaseAnterior.Equals(usuario.cRol, StringComparison.OrdinalIgnoreCase))
        {
            var overrides = await _context.UsuarioPermisos.Where(item => item.nUsuario == id).ToListAsync();
            _context.UsuarioPermisos.RemoveRange(overrides);
        }

        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync("Usuario", id, "EDICION", anterior, nombre, UsuarioActualId);
        return Ok(new { success = true, nombre = usuario.cNombre, rol = usuario.cRol, rolId = usuario.nRol });
    }

    [HttpDelete("usuarios/{id:int}")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> EliminarUsuario(int id)
    {
        if (UsuarioActualId == id)
            return BadRequest("No puedes eliminar la cuenta con la que has iniciado sesión.");

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(item => item.nUsuario == id && item.cEstado == 'A');
        if (usuario == null) return NotFound("Usuario no encontrado.");
        if (CrmRoles.Normalize(usuario.cRol).Equals(CrmRoles.Administrador, StringComparison.OrdinalIgnoreCase))
            return BadRequest("La cuenta Administrador principal no se puede eliminar.");

        usuario.cEstado = 'I';
        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync("Usuario", id, "ELIMINACION", usuario.cNombre, null, UsuarioActualId);
        return Ok(new { success = true });
    }

    [HttpGet("usuarios/{id:int}/permisos")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> ObtenerPermisosUsuario(int id)
    {
        var usuario = await _context.Usuarios.AsNoTracking().FirstOrDefaultAsync(item => item.nUsuario == id);
        if (usuario == null) return NotFound("Usuario no encontrado.");

        var granted = await _context.UsuarioPermisos.AsNoTracking()
            .Where(item => item.nUsuario == id)
            .Select(item => item.cPermiso)
            .ToListAsync();
        var basePermissions = await _permissions.GetInheritedPermissionsAsync(id, usuario.cRol);
        var effective = CrmPermissionService.ApplyOverrides(basePermissions, granted);

        return Ok(new
        {
            usuario = new { id = usuario.nUsuario, nombre = usuario.cNombre, rol = usuario.cRol },
            permisos = CrmPermissionService.Catalog.Select(item => new
            {
                codigo = item.Code,
                grupo = item.Group,
                nombre = item.Label,
                descripcion = item.Description,
                heredado = basePermissions.Contains(item.Code),
                adicional = granted.Contains(item.Code, StringComparer.OrdinalIgnoreCase),
                efectivo = effective.Contains(item.Code)
            })
        });
    }

    [HttpPut("usuarios/{id:int}/permisos")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> GuardarPermisosUsuario(int id, [FromBody] ActualizarPermisosDto dto)
    {
        var usuario = await _context.Usuarios.FirstOrDefaultAsync(item => item.nUsuario == id);
        if (usuario == null) return NotFound("Usuario no encontrado.");
        if (CrmRoles.Normalize(usuario.cRol).Equals(CrmRoles.Administrador, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("El Administrador conserva siempre todos los permisos.");
        }

        if (dto.Permisos == null) return BadRequest("Debes enviar la lista de permisos seleccionados.");
        if (dto.Permisos.Any(item => !CrmPermissionService.IsConfigurable(item)))
            return BadRequest("La lista contiene permisos no válidos.");

        var requested = dto.Permisos
            .Where(CrmPermissionService.IsConfigurable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // El editor nuevo envía el conjunto efectivo completo. Se mantiene
        // compatible el contrato anterior, que sólo concedía adicionales.
        var stored = dto.Personalizar
            ? CrmPermissionService.BuildOverrides(await _permissions.GetInheritedPermissionsAsync(id, usuario.cRol), requested)
            : requested.Except(await _permissions.GetInheritedPermissionsAsync(id, usuario.cRol), StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await _context.UsuarioPermisos.Where(item => item.nUsuario == id).ToListAsync();
        var previous = existing.Select(item => item.cPermiso).OrderBy(item => item).ToArray();
        _context.UsuarioPermisos.RemoveRange(existing.Where(item => !stored.Contains(item.cPermiso)));

        var current = existing.Select(item => item.cPermiso).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var permission in stored.Where(item => !current.Contains(item)))
        {
            _context.UsuarioPermisos.Add(new UsuarioPermiso
            {
                nUsuario = id,
                cPermiso = permission,
                nOtorgadoPor = UsuarioActualId,
                dFechaAsignacion = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync(
            "UsuarioPermisos",
            id,
            "CAMBIO_PERMISOS",
            string.Join(", ", previous),
            string.Join(", ", stored.OrderBy(item => item)),
            UsuarioActualId);

        return Ok(new { success = true, permisos = CrmPermissionService.ApplyOverrides(
            await _permissions.GetInheritedPermissionsAsync(id, usuario.cRol), stored).OrderBy(item => item) });
    }

    [HttpPut("conversaciones/{id:long}/estado")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> CambiarEstado(long id, [FromBody] EstadoDto dto)
    {
        var estado = (dto.Estado ?? string.Empty).Trim().ToUpperInvariant();
        if (!EstadosConversacionPermitidos.Contains(estado))
        {
            return BadRequest("Estado no válido.");
        }

        var conversacion = await _context.Conversaciones.FindAsync(id);
        if (conversacion == null)
        {
            return NotFound("Conversación no encontrada.");
        }

        if (!await _access.PuedeAccederConversacionAsync(id))
        {
            return Forbid();
        }

        if (estado is "ESPERANDO_CLIENTE" or "COTIZACION_ENVIADA")
        {
            var tieneProximoPaso = await _context.Tareas
                .AsNoTracking()
                .AnyAsync(tarea =>
                    tarea.nConversacion == id &&
                    tarea.cEstado == "PENDIENTE" &&
                    tarea.dFechaVencimiento >= DateTime.Today);

            if (!tieneProximoPaso)
            {
                return BadRequest(
                    "Antes de dejar este lead en espera o cotización, crea una tarea con el próximo paso. " +
                    "Así ningún prospecto queda sin seguimiento.");
            }
        }

        if (estado == "CERRADO")
        {
            var tieneTareasAbiertas = await _context.Tareas
                .AsNoTracking()
                .AnyAsync(tarea =>
                    tarea.nConversacion == id &&
                    tarea.cEstado != "COMPLETADA" &&
                    tarea.cEstado != "CANCELADA");

            var tieneOportunidadesAbiertas = await _context.Oportunidades
                .AsNoTracking()
                .AnyAsync(oportunidad =>
                    oportunidad.nConversacion == id &&
                    oportunidad.cEtapa != "GANADA" &&
                    oportunidad.cEtapa != "PERDIDA");

            if (tieneTareasAbiertas || tieneOportunidadesAbiertas)
            {
                return BadRequest(
                    "No se puede cerrar la conversación mientras tenga tareas u oportunidades abiertas. " +
                    "Completa o reprograma la próxima acción antes de cerrarla.");
            }
        }

        var estadoAnterior = conversacion.cEstado;
        conversacion.cEstado = estado;
        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync("Conversacion", id, "CAMBIO_ESTADO", estadoAnterior, estado, UsuarioActualId);
        return Ok(new { success = true, id, estado });
    }

    [HttpPut("conversaciones/{id:long}/tomar")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> TomarConversacion(long id)
    {
        if (!UsuarioActualId.HasValue)
        {
            return Unauthorized();
        }

        if (!await _access.PuedeAccederConversacionAsync(id)) return Forbid();

        var conversacion = await _context.Conversaciones.FindAsync(id);
        if (conversacion == null)
        {
            return NotFound("Conversación no encontrada.");
        }

        if (!_access.TieneAccesoGlobal &&
            conversacion.nUsuarioAsignado.HasValue &&
            conversacion.nUsuarioAsignado != UsuarioActualId)
        {
            return Forbid();
        }

        var asignadoAnterior = conversacion.nUsuarioAsignado?.ToString() ?? "sin asignar";
        var estadoAnterior = conversacion.cEstado;

        conversacion.nUsuarioAsignado = UsuarioActualId.Value;
        if (conversacion.cEstado is "NUEVO" or "ABIERTO")
        {
            conversacion.cEstado = "EN_ATENCION";
        }

        conversacion.cBotEstado = "PAUSADO";
        conversacion.dBotPausadoDesde = DateTime.Now;
        conversacion.nBotPausadoPor = UsuarioActualId.Value;

        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync("Conversacion", id, "TOMADA", asignadoAnterior,
            UsuarioActualId.Value.ToString(), UsuarioActualId);

        if (estadoAnterior != conversacion.cEstado)
        {
            await _auditoria.RegistrarAsync("Conversacion", id, "CAMBIO_ESTADO", estadoAnterior,
                conversacion.cEstado, UsuarioActualId);
        }

        return Ok(new
        {
            success = true,
            id,
            usuarioId = UsuarioActualId.Value,
            estado = conversacion.cEstado
        });
    }

    [HttpPut("conversaciones/{id:long}/asignar")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> Asignar(long id, [FromBody] AsignacionDto dto)
    {
        var conversacion = await _context.Conversaciones.FindAsync(id);
        if (conversacion == null)
        {
            return NotFound("Conversación no encontrada.");
        }

        if (dto.UsuarioId.HasValue)
        {
            var existe = await _context.Usuarios.AnyAsync(usuario =>
                usuario.nUsuario == dto.UsuarioId.Value && usuario.cEstado == 'A');
            if (!existe)
            {
                return BadRequest("El asesor no existe o está inactivo.");
            }
        }

        var asignadoAnterior = conversacion.nUsuarioAsignado?.ToString() ?? "sin asignar";
        conversacion.nUsuarioAsignado = dto.UsuarioId;
        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync("Conversacion", id, "REASIGNACION", asignadoAnterior,
            dto.UsuarioId?.ToString() ?? "sin asignar", UsuarioActualId);
        return Ok(new { success = true, id, usuarioId = dto.UsuarioId });
    }

    [HttpPost("conversaciones/{id:long}/solicitar-reasignacion")]
    [Authorize(Roles = "Asesor")]
    public async Task<IActionResult> SolicitarReasignacion(long id)
    {
        if (!UsuarioActualId.HasValue)
        {
            return Unauthorized();
        }

        var conversacion = await _context.Conversaciones
            .FirstOrDefaultAsync(item => item.nConversacion == id);
        if (conversacion == null)
        {
            return NotFound("Conversación no encontrada.");
        }

        if (conversacion.nUsuarioAsignado != UsuarioActualId.Value)
        {
            return Forbid();
        }

        if (conversacion.cEstado is "CERRADO" or "PERDIDO" or "NO_RESPONDIO")
        {
            return BadRequest("No se puede solicitar la reasignación de una conversación finalizada.");
        }

        var asignadoAnterior = conversacion.nUsuarioAsignado.Value.ToString();
        conversacion.nUsuarioAsignado = null;
        conversacion.cEstado = "ABIERTO";
        conversacion.cBotEstado = "ACTIVO";
        conversacion.dBotPausadoDesde = null;
        conversacion.nBotPausadoPor = null;

        _context.NotasInternas.Add(new NotaInterna
        {
            nCliente = conversacion.nCliente,
            nConversacion = conversacion.nConversacion,
            cTexto = "El asesor solicitó transferir esta conversación. Quedó pendiente de reasignación.",
            nCreadoPor = UsuarioActualId.Value,
            dFecha = DateTime.Now
        });

        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync(
            "Conversacion",
            id,
            "SOLICITUD_REASIGNACION",
            asignadoAnterior,
            "sin asignar",
            UsuarioActualId);

        return Ok(new
        {
            success = true,
            id,
            estado = conversacion.cEstado,
            mensaje = "La conversación quedó disponible para que un supervisor la reasigne."
        });
    }

    [HttpPost("conversaciones/{id:long}/actualizar-perfil-meta")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> ActualizarPerfilMeta(long id)
    {
        var conversacion = await _context.Conversaciones
            .Include(item => item.Cliente)
            .FirstOrDefaultAsync(item => item.nConversacion == id);

        if (conversacion == null)
        {
            return NotFound("Conversación no encontrada.");
        }

        if (!await _access.PuedeAccederConversacionAsync(id))
        {
            return Forbid();
        }

        if (conversacion.cCanal is not (CanalSocial.Facebook or CanalSocial.Instagram))
        {
            return BadRequest("Esta acción solo aplica para Facebook e Instagram.");
        }

        var externalId = conversacion.cExternalThreadId ?? conversacion.Cliente.cTelefono;
        var result = await _metaGraph.ObtenerPerfilContactoDetalladoAsync(conversacion.cCanal, externalId);
        if (!result.Success || result.Profile == null)
        {
            return Ok(new
            {
                success = false,
                updated = false,
                error = result.Error,
                configuredPageId = result.ConfiguredPageId,
                webhookPageId = result.WebhookPageId
            });
        }

        var nombre = result.Profile.DisplayName ?? result.Profile.Username;
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Ok(new
            {
                success = false,
                updated = false,
                error = "Meta devolvió el perfil, pero sin nombre visible."
            });
        }

        var nombreAnterior = conversacion.Cliente.cNombre;
        conversacion.Cliente.cNombre = nombre.Trim();
        if (!string.IsNullOrWhiteSpace(result.Profile.ProfilePictureUrl))
        {
            conversacion.Cliente.cFotoPerfilUrl = result.Profile.ProfilePictureUrl.Trim();
        }
        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync(
            "Cliente",
            conversacion.Cliente.nCliente,
            "ACTUALIZACION_PERFIL_META",
            nombreAnterior,
            conversacion.Cliente.cNombre,
            UsuarioActualId);

        return Ok(new
        {
            success = true,
            updated = true,
            clienteId = conversacion.Cliente.nCliente,
            nombre = conversacion.Cliente.cNombre,
            fotoPerfilUrl = conversacion.Cliente.cFotoPerfilUrl
        });
    }

    // =========================================================
    // ASIGNAR PENDIENTES (BACKFILL)
    // =========================================================
    //
    // Reparte de una sola vez todas las conversaciones NUEVO/
    // ABIERTO que quedaron "Sin asignar" (por ejemplo, las que ya
    // existían antes de activar el reparto automático). De ahí en
    // adelante, cada mensaje nuevo de un cliente se asigna solo.
    //
    // =========================================================

    [HttpPost("conversaciones/asignar-pendientes")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> AsignarPendientes()
    {
        var cantidad = await _whatsappService.AsignarConversacionesPendientesAsync();
        return Ok(new { success = true, asignadas = cantidad });
    }

    [HttpPut("contactos/{id:long}")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> ActualizarContacto(long id, [FromBody] ContactoDto dto)
    {
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente == null)
        {
            return NotFound("Cliente no encontrado.");
        }

        // Un asesor puede guardar la ficha de sus propios clientes y también
        // la de un chat activo sin asignar que esté disponible para tomar.
        if (!_access.TieneAccesoGlobal && !await _access.PuedeAccederClienteAsync(id))
        {
            return Forbid();
        }

        var datosAnteriores = $"{cliente.cNombre} / {cliente.cTelefono} / {cliente.cEmail}";
        if (!string.IsNullOrWhiteSpace(dto.Nombre)) cliente.cNombre = dto.Nombre.Trim();
        if (!string.IsNullOrWhiteSpace(dto.Telefono)) cliente.cTelefono = dto.Telefono.Trim();
        cliente.cEmail = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        cliente.cDocumento = string.IsNullOrWhiteSpace(dto.Documento) ? null : dto.Documento.Trim();
        await _context.SaveChangesAsync();
        await _auditoria.RegistrarAsync("Cliente", id, "EDICION", datosAnteriores,
            $"{cliente.cNombre} / {cliente.cTelefono} / {cliente.cEmail}", UsuarioActualId);
        return Ok(new { success = true, id = cliente.nCliente });
    }

    [HttpGet("actividad")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> Actividad(
        [FromQuery] string? entidad = null,
        [FromQuery] long? entidadId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = _context.ActividadLogs.AsNoTracking().AsQueryable();
        if (!_access.TieneAccesoGlobal && !_access.EsAuditor)
        {
            var conversacionesAccesibles = _access
                .FiltrarConversaciones(_context.Conversaciones.AsNoTracking())
                .Select(conversacion => conversacion.nConversacion);
            var clientesAccesibles = _access
                .FiltrarClientes(_context.Clientes.AsNoTracking())
                .Select(cliente => cliente.nCliente);

            query = query.Where(log =>
                (log.cEntidad == "Conversacion" && conversacionesAccesibles.Contains(log.nEntidadId)) ||
                (log.cEntidad == "Cliente" && clientesAccesibles.Contains(log.nEntidadId)));
        }

        if (!string.IsNullOrWhiteSpace(entidad)) query = query.Where(a => a.cEntidad == entidad);
        if (entidadId.HasValue) query = query.Where(a => a.nEntidadId == entidadId.Value);

        var total = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await query
            .Include(a => a.Usuario)
            .OrderByDescending(a => a.dFecha)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                id = a.nActividad,
                entidad = a.cEntidad,
                entidadId = a.nEntidadId,
                accion = a.cAccion,
                anterior = a.cValorAnterior,
                nuevo = a.cValorNuevo,
                usuario = a.Usuario == null ? "sistema" : a.Usuario.cNombre,
                fecha = a.dFecha
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("contactos/exportar")]
    public async Task<IActionResult> ExportarContactos(
        [FromQuery] string? search = null,
        [FromQuery] string? canal = null,
        [FromQuery] int? usuarioId = null,
        [FromQuery] int? etiquetaId = null)
    {
        if (etiquetaId.HasValue && (!UsuarioActualId.HasValue ||
            !await _permissions.HasAsync(UsuarioActualId.Value, _access.RolActual, CrmPermissionService.ViewCustomerDetails))) return Forbid();
        var query = _access.FiltrarClientes(_context.Clientes.AsNoTracking());
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(cliente =>
                cliente.cNombre.Contains(search) ||
                cliente.cTelefono.Contains(search) ||
                (cliente.cEmail != null && cliente.cEmail.Contains(search)) ||
                (cliente.cDocumento != null && cliente.cDocumento.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(canal) && !canal.Equals("TODOS", StringComparison.OrdinalIgnoreCase))
        {
            var canalNormalizado = CanalSocial.Normalizar(canal);
            query = query.Where(cliente => cliente.cCanalOrigen == canalNormalizado || cliente.Conversaciones.Any(c => c.cCanal == canalNormalizado));
        }

        if (usuarioId.HasValue && _access.TieneAccesoGlobal) query = query.Where(cliente => cliente.Conversaciones.Any(c => c.nUsuarioAsignado == usuarioId.Value));
        if (etiquetaId.HasValue) query = query.Where(cliente => _context.ClienteEtiquetas.Any(ce => ce.nCliente == cliente.nCliente && ce.nEtiqueta == etiquetaId.Value));

        var filas = await query
            .OrderBy(cliente => cliente.cNombre)
            .Select(cliente => new string?[]
            {
                cliente.cNombre,
                cliente.cTelefono,
                cliente.cEmail,
                cliente.cDocumento,
                cliente.cCanalOrigen,
                cliente.Conversaciones.Count.ToString(),
                cliente.Conversaciones.OrderByDescending(c => c.dUltimoMensaje).Select(c => c.dUltimoMensaje).FirstOrDefault().ToString()
            })
            .ToListAsync();

        return CsvFile("contactos-crm.csv", new[] { "Nombre", "Telefono", "Email", "Documento", "Canal", "Conversaciones", "Ultima actividad" }, filas);
    }

    [HttpGet("fallos")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    [CrmPermission(CrmPermissionService.ModuleFailures)]
    public async Task<IActionResult> Fallos([FromQuery] int page = 1, [FromQuery] int pageSize = 100)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 100 : pageSize;

        var mensajesFallidos = _context.Mensajes
            .AsNoTracking()
            .Where(mensaje => mensaje.cEstado != null &&
                (mensaje.cEstado.Contains("FALLIDO") || mensaje.cEstado.Contains("ERROR") || mensaje.cEstado.Contains("LOCAL")))
            .Select(mensaje => new
            {
                tipo = "Mensaje",
                id = mensaje.nMensaje,
                canal = mensaje.cCanal,
                detalle = mensaje.cEstado,
                texto = (string?)mensaje.cMensaje,
                fecha = mensaje.dFecha,
                conversacionId = (long?)mensaje.nConversacion,
                entidadId = (long?)mensaje.nMensaje,
                clienteId = (long?)mensaje.Conversacion.nCliente,
                clienteNombre = (string?)mensaje.Conversacion.Cliente.cNombre,
                clienteTelefono = (string?)mensaje.Conversacion.Cliente.cTelefono,
                conversacionEstado = (string?)mensaje.Conversacion.cEstado,
                direccion = (string?)(mensaje.cDireccion == 'E' ? "Entrante" : "Saliente"),
                tipoMensaje = (string?)mensaje.cTipo,
                externalId = (string?)mensaje.cExternalId
            });

        var eventosFallidos = _context.ActividadLogs
            .AsNoTracking()
            .Where(log => log.cEntidad == "Integracion" || log.cAccion.Contains("WEBHOOK") || log.cAccion.Contains("ERROR"))
            .Select(log => new
            {
                tipo = log.cEntidad,
                id = log.nActividad,
                canal = log.cAccion,
                detalle = log.cValorNuevo,
                texto = log.cValorAnterior,
                fecha = log.dFecha,
                conversacionId = (long?)null,
                entidadId = (long?)log.nEntidadId,
                clienteId = (long?)null,
                clienteNombre = (string?)null,
                clienteTelefono = (string?)null,
                conversacionEstado = (string?)null,
                direccion = (string?)null,
                tipoMensaje = (string?)null,
                externalId = (string?)null
            });

        var query = mensajesFallidos.Concat(eventosFallidos);
        var total = await query.CountAsync();
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await query.OrderByDescending(item => item.fecha).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("actividad/historial")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> HistorialActividad(
        [FromQuery] long? clienteId = null,
        [FromQuery] long? conversacionId = null,
        [FromQuery] int? usuarioId = null,
        [FromQuery] string? entidad = null,
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = _context.ActividadLogs
            .AsNoTracking()
            .Include(log => log.Usuario)
            .AsQueryable();

        if (clienteId.HasValue)
        {
            query = query.Where(log => log.cEntidad == "Cliente" && log.nEntidadId == clienteId.Value);
        }

        if (conversacionId.HasValue)
        {
            query = query.Where(log => log.cEntidad == "Conversacion" && log.nEntidadId == conversacionId.Value);
        }

        if (usuarioId.HasValue)
        {
            query = query.Where(log => log.nUsuario == usuarioId.Value);
        }

        if (!string.IsNullOrWhiteSpace(entidad))
        {
            var entidadNormalizada = entidad.Trim();
            query = query.Where(log => log.cEntidad == entidadNormalizada);
        }

        if (desde.HasValue)
        {
            query = query.Where(log => log.dFecha >= desde.Value.Date);
        }

        if (hasta.HasValue)
        {
            var hastaProxima = hasta.Value.Date.AddDays(1);
            query = query.Where(log => log.dFecha < hastaProxima);
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(log => log.dFecha)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new
            {
                id = log.nActividad,
                entidad = log.cEntidad,
                entidadId = log.nEntidadId,
                accion = log.cAccion,
                valorAnterior = log.cValorAnterior,
                valorNuevo = log.cValorNuevo,
                usuarioId = log.nUsuario,
                usuarioNombre = log.Usuario != null ? log.Usuario.cNombre : null,
                fecha = log.dFecha
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    private async Task<object[]> ObtenerCargaAsesoresAsync(int? usuarioId)
    {
        var usuariosQuery = _context.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.cEstado == 'A');
        if (usuarioId.HasValue)
        {
            usuariosQuery = usuariosQuery.Where(usuario => usuario.nUsuario == usuarioId.Value);
        }

        var usuarios = await usuariosQuery
            .Select(usuario => new
            {
                usuarioId = usuario.nUsuario,
                nombre = usuario.cNombre,
                rol = usuario.cRol
            })
            .ToListAsync();

        var conversacionesActivasQuery = _context.Conversaciones
            .AsNoTracking()
            .Where(conversacion =>
                conversacion.nUsuarioAsignado.HasValue &&
                conversacion.cEstado != "CERRADO" &&
                conversacion.cEstado != "PERDIDO" &&
                conversacion.cEstado != "NO_RESPONDIO");
        if (usuarioId.HasValue)
        {
            conversacionesActivasQuery = conversacionesActivasQuery.Where(conversacion => conversacion.nUsuarioAsignado == usuarioId.Value);
        }

        var conversacionesActivasPorAsesor = await conversacionesActivasQuery
            .GroupBy(conversacion => conversacion.nUsuarioAsignado!.Value)
            .Select(grupo => new { usuarioId = grupo.Key, conversacionesActivas = grupo.Count() })
            .ToListAsync();

        var tareasPorAsesorQuery = _context.Tareas
            .AsNoTracking()
            .Where(tarea => tarea.nAsignadoA.HasValue && tarea.cEstado == "PENDIENTE");
        if (usuarioId.HasValue)
        {
            tareasPorAsesorQuery = tareasPorAsesorQuery.Where(tarea => tarea.nAsignadoA == usuarioId.Value);
        }

        var tareasPorAsesor = await tareasPorAsesorQuery
            .GroupBy(tarea => tarea.nAsignadoA!.Value)
            .Select(grupo => new
            {
                usuarioId = grupo.Key,
                tareasPendientes = grupo.Count(),
                tareasVencidas = grupo.Count(tarea => tarea.dFechaVencimiento < DateTime.Now)
            })
            .ToListAsync();

        var oportunidadesPorAsesorQuery = _context.Oportunidades
            .AsNoTracking()
            .Where(oportunidad =>
                oportunidad.nUsuarioAsignado.HasValue &&
                oportunidad.cEtapa != "GANADA" &&
                oportunidad.cEtapa != "PERDIDA");
        if (usuarioId.HasValue)
        {
            oportunidadesPorAsesorQuery = oportunidadesPorAsesorQuery.Where(oportunidad => oportunidad.nUsuarioAsignado == usuarioId.Value);
        }

        var oportunidadesPorAsesor = await oportunidadesPorAsesorQuery
            .GroupBy(oportunidad => oportunidad.nUsuarioAsignado!.Value)
            .Select(grupo => new
            {
                usuarioId = grupo.Key,
                oportunidadesAbiertas = grupo.Count(),
                montoAbierto = grupo.Sum(oportunidad => oportunidad.nMonto)
            })
            .ToListAsync();

        var cargaAsesores = usuarios
            .Select(usuario =>
            {
                var conversacionesActivas = conversacionesActivasPorAsesor
                    .FirstOrDefault(item => item.usuarioId == usuario.usuarioId)?.conversacionesActivas ?? 0;
                var tareas = tareasPorAsesor
                    .FirstOrDefault(item => item.usuarioId == usuario.usuarioId);
                var oportunidades = oportunidadesPorAsesor
                    .FirstOrDefault(item => item.usuarioId == usuario.usuarioId);

                return new
                {
                    usuario.usuarioId,
                    usuario.nombre,
                    usuario.rol,
                    conversacionesActivas,
                    tareasPendientes = tareas?.tareasPendientes ?? 0,
                    tareasVencidas = tareas?.tareasVencidas ?? 0,
                    oportunidadesAbiertas = oportunidades?.oportunidadesAbiertas ?? 0,
                    montoAbierto = oportunidades?.montoAbierto ?? 0m,
                    cargaTotal = conversacionesActivas + (tareas?.tareasPendientes ?? 0) + (oportunidades?.oportunidadesAbiertas ?? 0)
                };
            })
            .OrderByDescending(item => item.cargaTotal)
            .ThenBy(item => item.nombre)
            .ToList();
        return cargaAsesores.Cast<object>().ToArray();
    }

    [HttpGet("reportes/carga-asesores")]
    public async Task<IActionResult> CargaAsesores([FromQuery] int? usuarioId = null)
    {
        if (!_access.TieneAccesoGlobal)
        {
            if (!UsuarioActualId.HasValue || (usuarioId.HasValue && usuarioId != UsuarioActualId)) return Forbid();
            usuarioId = UsuarioActualId;
        }
        return Ok(await ObtenerCargaAsesoresAsync(usuarioId));
    }

    [HttpGet("reportes/resumen")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> ReporteResumen(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] int? usuarioId = null,
        [FromQuery] bool incluirEquipo = true)
    {
        if (!_access.TieneAccesoGlobal)
        {
            if (!UsuarioActualId.HasValue)
            {
                return Forbid();
            }

            if (usuarioId.HasValue && usuarioId.Value != UsuarioActualId.Value)
            {
                return Forbid();
            }

            usuarioId = UsuarioActualId.Value;
        }

        var fechaDesde = desde?.Date;
        var fechaHasta = hasta?.Date;
        var fechaHastaExclusiva = fechaHasta?.AddDays(1);

        var clientesQuery = _context.Clientes.AsNoTracking().AsQueryable();
        if (fechaDesde.HasValue) clientesQuery = clientesQuery.Where(cliente => cliente.dFechaRegistro >= fechaDesde.Value);
        if (fechaHastaExclusiva.HasValue) clientesQuery = clientesQuery.Where(cliente => cliente.dFechaRegistro < fechaHastaExclusiva.Value);
        if (usuarioId.HasValue)
        {
            clientesQuery = clientesQuery.Where(cliente =>
                cliente.Conversaciones.Any(conversacion => conversacion.nUsuarioAsignado == usuarioId.Value));
        }

        var conversacionesQuery = _context.Conversaciones.AsNoTracking().AsQueryable();
        if (fechaDesde.HasValue) conversacionesQuery = conversacionesQuery.Where(conversacion => conversacion.dFechaInicio >= fechaDesde.Value);
        if (fechaHastaExclusiva.HasValue) conversacionesQuery = conversacionesQuery.Where(conversacion => conversacion.dFechaInicio < fechaHastaExclusiva.Value);
        if (usuarioId.HasValue) conversacionesQuery = conversacionesQuery.Where(conversacion => conversacion.nUsuarioAsignado == usuarioId.Value);

        var mensajesQuery = _context.Mensajes.AsNoTracking().AsQueryable();
        if (fechaDesde.HasValue) mensajesQuery = mensajesQuery.Where(mensaje => mensaje.dFecha >= fechaDesde.Value);
        if (fechaHastaExclusiva.HasValue) mensajesQuery = mensajesQuery.Where(mensaje => mensaje.dFecha < fechaHastaExclusiva.Value);
        if (usuarioId.HasValue) mensajesQuery = mensajesQuery.Where(mensaje => mensaje.Conversacion.nUsuarioAsignado == usuarioId.Value);

        var oportunidadesQuery = _context.Oportunidades.AsNoTracking().AsQueryable();
        if (fechaDesde.HasValue) oportunidadesQuery = oportunidadesQuery.Where(oportunidad => oportunidad.dFechaCreacion >= fechaDesde.Value);
        if (fechaHastaExclusiva.HasValue) oportunidadesQuery = oportunidadesQuery.Where(oportunidad => oportunidad.dFechaCreacion < fechaHastaExclusiva.Value);
        if (usuarioId.HasValue) oportunidadesQuery = oportunidadesQuery.Where(oportunidad => oportunidad.nUsuarioAsignado == usuarioId.Value);

        var tareasQuery = _context.Tareas.AsNoTracking().AsQueryable();
        if (fechaDesde.HasValue) tareasQuery = tareasQuery.Where(tarea => tarea.dFechaCreacion >= fechaDesde.Value);
        if (fechaHastaExclusiva.HasValue) tareasQuery = tareasQuery.Where(tarea => tarea.dFechaCreacion < fechaHastaExclusiva.Value);
        if (usuarioId.HasValue) tareasQuery = tareasQuery.Where(tarea => tarea.nAsignadoA == usuarioId.Value);

        var estadosCanales = await conversacionesQuery
            .GroupBy(c => new { c.cEstado, c.cCanal })
            .Select(g => new { estado = g.Key.cEstado, canal = g.Key.cCanal, cantidad = g.Count() })
            .ToListAsync();
        var porEstado = estadosCanales.GroupBy(c => c.estado)
            .Select(g => new { estado = g.Key, cantidad = g.Sum(c => c.cantidad) }).ToList();
        var clientesPorCanal = await clientesQuery.GroupBy(c => c.cCanalOrigen)
            .Select(g => new { canal = g.Key, cantidad = g.Count() }).ToListAsync();
        var totalClientes = clientesPorCanal.Sum(c => c.cantidad);
        var mensajesPorCanal = await mensajesQuery.GroupBy(m => m.cCanal)
            .Select(g => new
            {
                canal = g.Key, interacciones = g.Count(),
                entrantes = g.Count(m => m.cDireccion == 'E'),
                salientes = g.Count(m => m.cDireccion == 'S')
            }).ToListAsync();
        var totalMensajes = mensajesPorCanal.Sum(m => m.interacciones);
        var mensajesEntrantes = mensajesPorCanal.Sum(m => m.entrantes);
        var mensajesSalientes = mensajesPorCanal.Sum(m => m.salientes);
        var oportunidadesPorEtapa = await oportunidadesQuery
            .GroupBy(oportunidad => oportunidad.cEtapa)
            .Select(grupo => new
            {
                etapa = grupo.Key,
                cantidad = grupo.Count(),
                montoTotal = grupo.Sum(oportunidad => oportunidad.nMonto)
            })
            .ToListAsync();
        var ahora = DateTime.Now;
        var estadisticasTareas = await tareasQuery
            .GroupBy(_ => 1)
            .Select(grupo => new
            {
                pendientes = grupo.Count(tarea => tarea.cEstado == "PENDIENTE"),
                vencidas = grupo.Count(tarea =>
                    tarea.cEstado == "PENDIENTE" && tarea.dFechaVencimiento < ahora),
                completadas = grupo.Count(tarea => tarea.cEstado == "COMPLETADA")
            })
            .FirstOrDefaultAsync();
        var tareasPendientes = estadisticasTareas?.pendientes ?? 0;
        var tareasVencidas = estadisticasTareas?.vencidas ?? 0;
        var tareasCompletadas = estadisticasTareas?.completadas ?? 0;
        var ventasGanadas = oportunidadesPorEtapa
            .Where(item => item.etapa == "GANADA")
            .Sum(item => item.montoTotal);
        var ventasAbiertas = oportunidadesPorEtapa
            .Where(item => item.etapa is not "GANADA" and not "PERDIDA")
            .Sum(item => item.montoTotal);
        var totalConversaciones = porEstado.Sum(item => item.cantidad);
        var oportunidadesAbiertas = oportunidadesPorEtapa
            .Where(item => item.etapa is not "GANADA" and not "PERDIDA")
            .Sum(item => item.cantidad);
        var oportunidadesGanadas = oportunidadesPorEtapa
            .Where(item => item.etapa == "GANADA")
            .Sum(item => item.cantidad);
        var oportunidadesPerdidas = oportunidadesPorEtapa
            .Where(item => item.etapa == "PERDIDA")
            .Sum(item => item.cantidad);

        var cargaAsesores = incluirEquipo ? await ObtenerCargaAsesoresAsync(usuarioId) : [];

        var conversacionesPorCanal = estadosCanales.GroupBy(c => c.canal)
            .Select(g => new { canal = g.Key, cantidad = g.Sum(c => c.cantidad) }).ToList();

        var canales = CanalSocial.Soportados.Select(canal =>
        {
            var mensajesCanal = mensajesPorCanal.FirstOrDefault(item => item.canal == canal);
            return new
            {
                canal,
                nombre = canal switch
                {
                    CanalSocial.Instagram => "Instagram",
                    CanalSocial.Facebook => "Facebook",
                    CanalSocial.TikTok => "TikTok",
                    _ => "WhatsApp"
                },
                conectado = canal == CanalSocial.WhatsApp,
                clientes = clientesPorCanal.FirstOrDefault(item => item.canal == canal)?.cantidad ?? 0,
                conversaciones = conversacionesPorCanal.FirstOrDefault(item => item.canal == canal)?.cantidad ?? 0,
                interacciones = mensajesCanal?.interacciones ?? 0,
                entrantes = mensajesCanal?.entrantes ?? 0,
                salientes = mensajesCanal?.salientes ?? 0,
                oportunidades = canal == CanalSocial.WhatsApp
                    ? oportunidadesAbiertas + oportunidadesGanadas + oportunidadesPerdidas
                    : 0,
                montoAbierto = canal == CanalSocial.WhatsApp ? ventasAbiertas : 0m,
                montoGanado = canal == CanalSocial.WhatsApp ? ventasGanadas : 0m,
                tareasPendientes = canal == CanalSocial.WhatsApp ? tareasPendientes : 0,
                tareasVencidas = canal == CanalSocial.WhatsApp ? tareasVencidas : 0
            };
        }).ToArray();

        return Ok(new
        {
            clientes = totalClientes,
            conversaciones = totalConversaciones,
            mensajes = totalMensajes,
            entrantes = mensajesEntrantes,
            salientes = mensajesSalientes,
            canales,
            porEstado,
            filtros = new
            {
                desde = fechaDesde,
                hasta = fechaHasta,
                usuarioId
            },
            cargaAsesores,
            tareas = new
            {
                pendientes = tareasPendientes,
                vencidas = tareasVencidas,
                completadas = tareasCompletadas
            },
            oportunidades = new
            {
                abiertas = oportunidadesAbiertas,
                ganadas = oportunidadesGanadas,
                perdidas = oportunidadesPerdidas,
                montoAbierto = ventasAbiertas,
                montoGanado = ventasGanadas,
                porEtapa = oportunidadesPorEtapa
            }
        });
    }

    [HttpGet("reportes/asesores")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> ReportePorAsesor(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null)
    {
        var fechaDesde = desde?.Date;
        var fechaHastaExclusiva = hasta?.Date.AddDays(1);

        var usuarios = await _context.Usuarios
            .AsNoTracking()
            .Where(usuario => usuario.cEstado == 'A')
            .OrderBy(usuario => usuario.cNombre)
            .Select(usuario => new
            {
                usuarioId = usuario.nUsuario,
                usuario = usuario.cNombre,
                rol = usuario.cRol,
                conversaciones = _context.Conversaciones
                    .AsNoTracking()
                    .Count(conversacion => conversacion.nUsuarioAsignado == usuario.nUsuario &&
                        (!fechaDesde.HasValue || conversacion.dFechaInicio >= fechaDesde.Value) &&
                        (!fechaHastaExclusiva.HasValue || conversacion.dFechaInicio < fechaHastaExclusiva.Value)),
                conversacionesActivas = _context.Conversaciones
                    .AsNoTracking()
                    .Count(conversacion => conversacion.nUsuarioAsignado == usuario.nUsuario &&
                        conversacion.cEstado != "CERRADO" && conversacion.cEstado != "PERDIDO" && conversacion.cEstado != "NO_RESPONDIO" &&
                        (!fechaDesde.HasValue || conversacion.dFechaInicio >= fechaDesde.Value) &&
                        (!fechaHastaExclusiva.HasValue || conversacion.dFechaInicio < fechaHastaExclusiva.Value)),
                tareasPendientes = _context.Tareas
                    .AsNoTracking()
                    .Count(tarea => tarea.nAsignadoA == usuario.nUsuario && tarea.cEstado == "PENDIENTE" &&
                        (!fechaDesde.HasValue || tarea.dFechaCreacion >= fechaDesde.Value) &&
                        (!fechaHastaExclusiva.HasValue || tarea.dFechaCreacion < fechaHastaExclusiva.Value)),
                tareasVencidas = _context.Tareas
                    .AsNoTracking()
                    .Count(tarea => tarea.nAsignadoA == usuario.nUsuario && tarea.cEstado == "PENDIENTE" &&
                        tarea.dFechaVencimiento < DateTime.Now &&
                        (!fechaDesde.HasValue || tarea.dFechaCreacion >= fechaDesde.Value) &&
                        (!fechaHastaExclusiva.HasValue || tarea.dFechaCreacion < fechaHastaExclusiva.Value)),
                oportunidadesAbiertas = _context.Oportunidades
                    .AsNoTracking()
                    .Count(oportunidad => oportunidad.nUsuarioAsignado == usuario.nUsuario &&
                        oportunidad.cEtapa != "GANADA" && oportunidad.cEtapa != "PERDIDA" &&
                        (!fechaDesde.HasValue || oportunidad.dFechaCreacion >= fechaDesde.Value) &&
                        (!fechaHastaExclusiva.HasValue || oportunidad.dFechaCreacion < fechaHastaExclusiva.Value)),
                oportunidadesGanadas = _context.Oportunidades
                    .AsNoTracking()
                    .Count(oportunidad => oportunidad.nUsuarioAsignado == usuario.nUsuario &&
                        oportunidad.cEtapa == "GANADA" &&
                        (!fechaDesde.HasValue || oportunidad.dFechaCreacion >= fechaDesde.Value) &&
                        (!fechaHastaExclusiva.HasValue || oportunidad.dFechaCreacion < fechaHastaExclusiva.Value)),
                montoGanado = _context.Oportunidades
                    .AsNoTracking()
                    .Where(oportunidad => oportunidad.nUsuarioAsignado == usuario.nUsuario && oportunidad.cEtapa == "GANADA" &&
                        (!fechaDesde.HasValue || oportunidad.dFechaCreacion >= fechaDesde.Value) &&
                        (!fechaHastaExclusiva.HasValue || oportunidad.dFechaCreacion < fechaHastaExclusiva.Value))
                    .Sum(oportunidad => (decimal?)oportunidad.nMonto) ?? 0m
            })
            .ToListAsync();

        return Ok(new
        {
            filtros = new { desde = fechaDesde, hasta = hasta },
            data = usuarios
        });
    }

    [HttpGet("reportes/canales")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> ReportePorCanal(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null)
    {
        var fechaDesde = desde?.Date;
        var fechaHastaExclusiva = hasta?.Date.AddDays(1);

        var canales = await _context.Conversaciones
            .AsNoTracking()
            .Where(conversacion =>
                (!fechaDesde.HasValue || conversacion.dFechaInicio >= fechaDesde.Value) &&
                (!fechaHastaExclusiva.HasValue || conversacion.dFechaInicio < fechaHastaExclusiva.Value))
            .GroupBy(conversacion => conversacion.cCanal)
            .Select(grupo => new
            {
                canal = grupo.Key,
                conversaciones = grupo.Count(),
                activas = grupo.Count(conversacion => conversacion.cEstado != "CERRADO" && conversacion.cEstado != "PERDIDO" && conversacion.cEstado != "NO_RESPONDIO"),
                cerradas = grupo.Count(conversacion => conversacion.cEstado == "CERRADO" || conversacion.cEstado == "PERDIDO"),
                clientes = _context.Clientes
                    .AsNoTracking()
                    .Count(cliente => cliente.cCanalOrigen == grupo.Key || cliente.Conversaciones.Any(conversacionCliente => conversacionCliente.cCanal == grupo.Key) &&
                        (!fechaDesde.HasValue || cliente.dFechaRegistro >= fechaDesde.Value) &&
                        (!fechaHastaExclusiva.HasValue || cliente.dFechaRegistro < fechaHastaExclusiva.Value))
            })
            .ToListAsync();

        return Ok(new
        {
            filtros = new { desde = fechaDesde, hasta = hasta },
            data = canales
        });
    }

    [HttpGet("reportes/exportar")]
    [Authorize(Roles = "Administrador,Supervisor,Asesor,Auditor,Marketing")]
    public async Task<IActionResult> ExportarReporte(
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        [FromQuery] int? usuarioId = null)
    {
        var resumen = await ReporteResumen(desde, hasta, usuarioId) as OkObjectResult;
        if (resumen?.Value == null)
        {
            return BadRequest("No se pudo generar el reporte.");
        }

        var json = System.Text.Json.JsonSerializer.Serialize(resumen.Value);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        var filas = new List<string?[]>
        {
            new[] { "Clientes", root.GetProperty("clientes").ToString() },
            new[] { "Conversaciones", root.GetProperty("conversaciones").ToString() },
            new[] { "Mensajes", root.GetProperty("mensajes").ToString() },
            new[] { "Entrantes", root.GetProperty("entrantes").ToString() },
            new[] { "Salientes", root.GetProperty("salientes").ToString() }
        };

        if (root.TryGetProperty("tareas", out var tareas))
        {
            filas.Add(new[] { "Tareas pendientes", tareas.GetProperty("pendientes").ToString() });
            filas.Add(new[] { "Tareas vencidas", tareas.GetProperty("vencidas").ToString() });
            filas.Add(new[] { "Tareas completadas", tareas.GetProperty("completadas").ToString() });
        }

        if (root.TryGetProperty("oportunidades", out var oportunidades))
        {
            filas.Add(new[] { "Oportunidades abiertas", oportunidades.GetProperty("abiertas").ToString() });
            filas.Add(new[] { "Oportunidades ganadas", oportunidades.GetProperty("ganadas").ToString() });
            filas.Add(new[] { "Oportunidades perdidas", oportunidades.GetProperty("perdidas").ToString() });
            filas.Add(new[] { "Monto abierto", oportunidades.GetProperty("montoAbierto").ToString() });
            filas.Add(new[] { "Monto ganado", oportunidades.GetProperty("montoGanado").ToString() });
        }

        return CsvFile("reporte-crm.csv", new[] { "Metrica", "Valor" }, filas);
    }

    private static IActionResult CsvFile(string fileName, string[] headers, IEnumerable<string?[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return new FileContentResult(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv; charset=utf-8")
        {
            FileDownloadName = fileName
        };
    }

    private static string EscapeCsv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
}

public sealed class EstadoDto
{
    [Required]
    public string? Estado { get; set; }
}

public sealed class AsignacionDto
{
    public int? UsuarioId { get; set; }
}

public sealed class ContactoDto
{
    private string? _email;
    private string? _documento;

    [StringLength(150)]
    public string? Nombre { get; set; }

    [StringLength(30)]
    public string? Telefono { get; set; }

    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(150)]
    public string? Email
    {
        get => _email;
        set => _email = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    [StringLength(20)]
    public string? Documento
    {
        get => _documento;
        set => _documento = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string? CanalOrigen { get; set; } = CanalSocial.WhatsApp;
}

public sealed class CrearUsuarioDto
{
    [Required(ErrorMessage = "El usuario es obligatorio.")]
    [StringLength(50)]
    public string? Usuario { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150)]
    public string? Nombre { get; set; }

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
    public string? Password { get; set; }

    public string? Rol { get; set; }
    public int? RolId { get; set; }
}

public sealed class CambiarPasswordDto
{
    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
    public string? Password { get; set; }
}

public sealed class EditarUsuarioDto
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150)]
    public string? Nombre { get; set; }

    public string? Password { get; set; }
    public string? Rol { get; set; }
    public int? RolId { get; set; }
}

public sealed class GuardarRolDto
{
    [Required]
    [StringLength(80)]
    public string? Nombre { get; set; }

    [StringLength(250)]
    public string? Descripcion { get; set; }

    public string? RolBase { get; set; }

    public List<string>? Permisos { get; set; }
}

public sealed class ActualizarPermisosDto
{
    public List<string>? Permisos { get; set; }
    public bool Personalizar { get; set; }
}
