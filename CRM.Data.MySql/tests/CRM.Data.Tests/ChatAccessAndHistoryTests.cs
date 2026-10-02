using System.Security.Claims;
using System.Text.Json;
using CRM.Data.Controllers;
using CRM.Data.Data;
using CRM.Data.Models;
using CRM.Data.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CRM.Data.Tests;

public sealed class ChatAccessAndHistoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContactTagsRequireFullCustomerDetailsPermission(bool allowed)
    {
        using var db = CreateContext();
        await Seed(db);
        db.Etiquetas.Add(new Etiqueta { nEtiqueta = 1, cNombre = "Reservado", cColor = "#00a884" });
        db.ClienteEtiquetas.Add(new ClienteEtiqueta { nCliente = 1, nEtiqueta = 1 });
        if (!allowed) db.UsuarioPermisos.Add(new UsuarioPermiso {
            nUsuario = 1, cPermiso = CrmPermissionService.DeniedPrefix + CrmPermissionService.ViewCustomerDetails
        });
        db.UsuarioPermisos.Add(new UsuarioPermiso { nUsuario = 1, cPermiso = CrmPermissionService.EditConversationContact });
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, CrmRoles.Asesor)
        ], "test")) };
        var controller = new CrmManagementController(db, null!, Service(db), null!, null!, Access(db, CrmRoles.Asesor), new CrmPermissionService(db)) {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        var list = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.Contactos(page: 1)).Value);
        Assert.Equal(allowed ? 1 : 0, list.GetProperty("items")[0].GetProperty("etiquetas").GetArrayLength());
        var search = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.Contactos(search: "Reservado", page: 1)).Value);
        Assert.Equal(allowed ? 1 : 0, search.GetProperty("total").GetInt32());
        var ordinarySearch = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.Contactos(search: "Cliente", page: 1)).Value);
        Assert.Equal(1, ordinarySearch.GetProperty("total").GetInt32());
        using var services = new ServiceCollection().AddSingleton(new CrmPermissionService(db)).BuildServiceProvider();
        http.RequestServices = services;
        foreach (var method in new[] { nameof(EtiquetasController.Listar), nameof(EtiquetasController.DeCliente) })
        {
            var filter = Assert.Single(typeof(EtiquetasController).GetMethod(method)!
                .GetCustomAttributes(typeof(CrmPermissionAttribute), false).Cast<CrmPermissionAttribute>());
            var authorization = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
            await filter.OnAuthorizationAsync(authorization);
            if (allowed) Assert.Null(authorization.Result);
            else Assert.IsType<ForbidResult>(authorization.Result);
        }
        if (allowed)
        {
            Assert.IsType<OkObjectResult>(await controller.Contactos(etiquetaId: 1, page: 1));
            Assert.IsType<FileContentResult>(await controller.ExportarContactos(etiquetaId: 1));
        }
        else
        {
            Assert.IsType<ForbidResult>(await controller.Contactos(etiquetaId: 1, page: 1));
            Assert.IsType<ForbidResult>(await controller.ExportarContactos(etiquetaId: 1));
        }
    }

    private static CrmDbContext CreateContext() => new(new DbContextOptionsBuilder<CrmDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CrmAccessService Access(CrmDbContext db, string role) => new(db,
        new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, role)
        ], "test")) } }, new CrmPermissionService(db));

    private static WhatsAppService Service(CrmDbContext db) =>
        new(db, null!, null!, null!, null!, null!, NullLogger<WhatsAppService>.Instance);

    private static async Task Seed(CrmDbContext db)
    {
        db.Usuarios.Add(new CrmUsuario { nUsuario = 1, cUsuario = "asesor", cNombre = "Asesor", cRol = CrmRoles.Asesor, cPasswordHash = "test" });
        db.Clientes.Add(new Cliente { nCliente = 1, cNombre = "Cliente", cTelefono = "test" });
        db.Conversaciones.AddRange(
            new Conversacion { nConversacion = 1, nCliente = 1, nUsuarioAsignado = 1 },
            new Conversacion { nConversacion = 2, nCliente = 1, nUsuarioAsignado = 2 },
            new Conversacion { nConversacion = 3, nCliente = 1, cEstado = "ABIERTO" },
            new Conversacion { nConversacion = 4, nCliente = 1, cEstado = "CERRADO" },
            new Conversacion { nConversacion = 5, nCliente = 1, nUsuarioAsignado = 2, cCanal = CanalSocial.Instagram });
        db.Mensajes.AddRange(Enumerable.Range(1, 130).Select(i => new Mensaje {
            nMensaje = i, nConversacion = 2, cDireccion = 'E', cTipo = "text", cMensaje = "Mensaje " + i,
            dFecha = new DateTime(2026, 10, 2, 10, 0, 0)
        }));
        db.Mensajes.Add(new Mensaje { nMensaje = 131, nConversacion = 5, cTipo = "text", cMensaje = "Instagram" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AdvisorWithAllChatsCanReadOthersButCannotManageThemOrBypassChannels()
    {
        using var db = CreateContext();
        await Seed(db);
        db.UsuarioPermisos.AddRange(
            new UsuarioPermiso { nUsuario = 1, cPermiso = CrmPermissionService.ViewAllChats },
            new UsuarioPermiso { nUsuario = 1, cPermiso = CrmPermissionService.DeniedPrefix + CrmPermissionService.ViewInstagram });
        await db.SaveChangesAsync();
        var access = Access(db, CrmRoles.Asesor);
        Assert.True(await access.PuedeVerTodasConversacionesAsync());
        Assert.Equal(130, await (await access.FiltrarMensajesLecturaAsync(db.Mensajes)).CountAsync());
        Assert.False(await access.PuedeAccederConversacionAsync(2));
        Assert.False(await access.PuedeAccederConversacionAsync(5));
        Assert.False(await access.PuedeControlarBotConversacionAsync(2));
        Assert.True(await access.PuedeAccederConversacionAsync(1));
        Assert.True(await access.PuedeAccederConversacionAsync(3));
        Assert.False(await access.PuedeAccederConversacionAsync(4));
    }

    [Fact]
    public async Task RevokingAllChatsRestrictsSupervisorToOwnAndAvailableChats()
    {
        using var db = CreateContext();
        await Seed(db);
        db.UsuarioPermisos.Add(new UsuarioPermiso { nUsuario = 1, cPermiso = CrmPermissionService.DeniedPrefix + CrmPermissionService.ViewAllChats });
        await db.SaveChangesAsync();
        var access = Access(db, CrmRoles.Supervisor);
        Assert.False(await access.PuedeVerTodasConversacionesAsync());
        Assert.False(await access.PuedeAccederConversacionAsync(2));
        Assert.Empty(await (await access.FiltrarMensajesLecturaAsync(db.Mensajes)).ToListAsync());
        Assert.True(await access.PuedeAccederConversacionAsync(1));
    }

    [Fact]
    public async Task HistoryPaginatesEqualTimestampsWithoutDuplicatesAndRejectsForeignCursor()
    {
        using var db = CreateContext();
        await Seed(db);
        var service = Service(db);
        var first = JsonSerializer.SerializeToElement(await service.ObtenerConversacionAsync(2, pageSize: 50, usuarioActualId: 1));
        var second = JsonSerializer.SerializeToElement(await service.ObtenerConversacionAsync(2, pageSize: 50, antesDe: first.GetProperty("mensajeMasAntiguoId").GetInt64()));
        var third = JsonSerializer.SerializeToElement(await service.ObtenerConversacionAsync(2, pageSize: 50, antesDe: second.GetProperty("mensajeMasAntiguoId").GetInt64()));
        var ids = new[] { first, second, third }.SelectMany(p => p.GetProperty("mensajes").EnumerateArray()).Select(m => m.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal(130, ids.Length);
        Assert.Equal(130, ids.Distinct().Count());
        Assert.True(first.GetProperty("hayMasMensajes").GetBoolean());
        Assert.False(third.GetProperty("hayMasMensajes").GetBoolean());
        Assert.False(first.GetProperty("puedeAtender").GetBoolean());
        Assert.Null(await service.ObtenerConversacionAsync(2, antesDe: 131));
        Assert.Null(await service.ObtenerConversacionAsync(2, usuarioAsignadoId: 1, incluirDisponiblesParaTomar: true));
        Assert.Null(await service.ObtenerConversacionAsync(5, canalesPermitidos: [CanalSocial.WhatsApp]));
    }

    [Fact]
    public async Task TextRetryReusesSavedMessageAcrossServiceInstances()
    {
        using var db = CreateContext();
        await Seed(db);
        db.Conversaciones.Single(c => c.nConversacion == 1).cCanal = CanalSocial.TikTok;
        await db.SaveChangesAsync();
        var key = Guid.NewGuid().ToString();
        var first = await Service(db).ProcesarMensajeSalienteAsync(1, "Prueba local", 1, "text", null, clientRequestId: key);
        db.ChangeTracker.Clear();
        var retry = await Service(db).ProcesarMensajeSalienteAsync(1, "Prueba local", 1, "text", null, clientRequestId: key);
        Assert.Equal(first, retry);
        Assert.Equal(1, await db.Mensajes.CountAsync(m => m.nConversacion == 1));
        Assert.NotNull(await db.Mensajes.Where(m => m.nMensaje == first).Select(m => m.cClientRequestId).SingleAsync());
    }

    [Fact]
    public async Task ListsReachRecordsBeyond200AndSummariesUseEntireAuthorizedDataset()
    {
        using var db = CreateContext();
        await Seed(db);
        db.Clientes.AddRange(Enumerable.Range(2, 204).Select(i => new Cliente { nCliente = i, cNombre = "Cliente " + i, cTelefono = "test" + i }));
        db.Tareas.AddRange(Enumerable.Range(1, 205).Select(i => new Tarea { nTarea = i, nAsignadoA = 1, cTitulo = "Tarea", dFechaVencimiento = DateTime.Today.AddDays(-1) }));
        db.Oportunidades.AddRange(Enumerable.Range(1, 205).Select(i => new Oportunidad { nOportunidad = i, nCliente = 1, nUsuarioAsignado = 1, cTitulo = "Venta", nMonto = 100m }));
        await db.SaveChangesAsync();
        var access = Access(db, CrmRoles.Administrador);
        var controllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, CrmRoles.Administrador)
        ], "test")) } };
        var crm = new CrmManagementController(db, null!, Service(db), null!, null!, access, new CrmPermissionService(db)) { ControllerContext = controllerContext };
        var tasks = new TareasController(db, access) { ControllerContext = controllerContext };
        var sales = new OportunidadesController(db, null!, access) { ControllerContext = controllerContext };
        var contactsPage = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await crm.Contactos(page: 5, pageSize: 50)).Value);
        var taskPage = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await tasks.Listar(page: 5, pageSize: 50, filtro: "vencidas")).Value);
        var salesPage = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await sales.Listar(page: 5, pageSize: 50)).Value);
        foreach (var page in new[] { contactsPage, taskPage, salesPage })
        {
            Assert.Equal(205, page.GetProperty("total").GetInt32());
            Assert.Equal(5, page.GetProperty("items").GetArrayLength());
        }
        Assert.Equal(205, taskPage.GetProperty("resumen").GetProperty("vencidas").GetInt32());
        Assert.Equal(20500m, salesPage.GetProperty("resumen").GetProperty("montoAbierto").GetDecimal());
        var summary = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await crm.ReporteResumen(incluirEquipo: false)).Value);
        Assert.Equal(205, summary.GetProperty("clientes").GetInt32());
        Assert.Equal(131, summary.GetProperty("mensajes").GetInt32());
        Assert.Empty(summary.GetProperty("cargaAsesores").EnumerateArray());
    }

    [Fact]
    public async Task InboxPaginatesAfterAuthorizationAndCountsPendingChatsBeyondCurrentPage()
    {
        using var db = CreateContext();
        await Seed(db);
        db.Conversaciones.AddRange(Enumerable.Range(6, 205).Select(i => new Conversacion {
            nConversacion = i, nCliente = 1, nUsuarioAsignado = 1, dUltimoMensaje = DateTime.Today.AddMinutes(i)
        }));
        db.Mensajes.AddRange(Enumerable.Range(6, 205).Select(i => new Mensaje {
            nMensaje = i + 200, nConversacion = i, cDireccion = 'E', cTipo = "text", cMensaje = "Entrante", dFecha = DateTime.Today
        }));
        db.Mensajes.AddRange(
            new Mensaje { nMensaje = 501, nConversacion = 6, cDireccion = 'S', cTipo = "bot", cMensaje = "Bot", dFecha = DateTime.Today.AddHours(1) },
            new Mensaje { nMensaje = 502, nConversacion = 7, cDireccion = 'S', cTipo = "text", cMensaje = "Respuesta", dFecha = DateTime.Today.AddHours(1) });
        await db.SaveChangesAsync();
        var page = JsonSerializer.SerializeToElement(await Service(db).ObtenerTodasConversacionesAsync(
            canalesPermitidos: [CanalSocial.WhatsApp], page: 5, pageSize: 50, usuarioActualId: 1));
        Assert.Equal(206, page.GetProperty("total").GetInt32());
        Assert.Equal(6, page.GetProperty("items").GetArrayLength());
        Assert.Equal(204, page.GetProperty("pendientes").GetInt32());
        var restricted = JsonSerializer.SerializeToElement(await Service(db).ObtenerTodasConversacionesAsync(
            usuarioAsignadoId: 1, incluirDisponiblesParaTomar: true, canalesPermitidos: [CanalSocial.WhatsApp], page: 5, pageSize: 50, usuarioActualId: 1));
        Assert.Equal(205, restricted.GetProperty("total").GetInt32());
        Assert.Equal(5, restricted.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain(restricted.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetInt64() == 2);
    }

    [Fact]
    public async Task UserDirectoryKeepsLegacyContractWhilePagedSearchPreservesGlobalSummary()
    {
        using var db = CreateContext();
        await Seed(db);
        db.Usuarios.AddRange(Enumerable.Range(2, 24).Select(i => new CrmUsuario {
            nUsuario = i, cUsuario = "usuario" + i, cNombre = "Usuario " + i,
            cRol = CrmRoles.Asesor, cEstado = 'A', cPasswordHash = "test"
        }));
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, CrmRoles.Administrador)
        ], "test")) };
        var controller = new CrmManagementController(db, null!, Service(db), null!, null!, Access(db, CrmRoles.Administrador), new CrmPermissionService(db)) {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
        var legacy = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.Usuarios()).Value);
        Assert.Equal(JsonValueKind.Array, legacy.ValueKind);
        Assert.Equal(25, legacy.GetArrayLength());
        var last = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.Usuarios(page: 999, pageSize: 10)).Value);
        Assert.Equal(3, last.GetProperty("page").GetInt32());
        Assert.Equal(5, last.GetProperty("items").GetArrayLength());
        var search = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.Usuarios(page: 1, pageSize: 10, search: "usuario24")).Value);
        Assert.Equal(1, search.GetProperty("total").GetInt32());
        Assert.Equal(24, search.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal(25, search.GetProperty("resumen").GetProperty("activos").GetInt32());
        var empty = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(await controller.Usuarios(page: 999, search: "not-present")).Value);
        Assert.Equal(1, empty.GetProperty("page").GetInt32());
        Assert.Empty(empty.GetProperty("items").EnumerateArray());
    }
}
