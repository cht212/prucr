// Dashboard operativo del CRM.
// Mantiene funciones globales porque otros módulos reutilizan iconos y helpers.

let dashboardCargaVersion = 0;
let dashboardFiltros = crearFiltrosDashboardIniciales();
const dashboardRenderCache = new Map();

function fechaLocalIso(fecha) {
    const year = fecha.getFullYear();
    const month = String(fecha.getMonth() + 1).padStart(2, "0");
    const day = String(fecha.getDate()).padStart(2, "0");
    return `${year}-${month}-${day}`;
}

function crearFiltrosDashboardIniciales() {
    const hoy = new Date();
    return {
        desde: fechaLocalIso(new Date(hoy.getFullYear(), hoy.getMonth(), 1)),
        hasta: fechaLocalIso(hoy),
        usuarioId: ""
    };
}

function formatearPeriodoDashboard(desde, hasta) {
    const formatear = valor => new Date(`${valor}T00:00:00`).toLocaleDateString("es-PE", {
        day: "2-digit", month: "short", year: "numeric"
    });
    return `${formatear(desde)} – ${formatear(hasta)}`;
}

function formatearDuracionDashboard(minutos) {
    const total = Math.max(0, Number(minutos || 0));
    if (total < 60) return `${total} min`;
    if (total < 1440) return `${Math.floor(total / 60)} h ${total % 60} min`;
    return `${Math.floor(total / 1440)} d ${Math.floor((total % 1440) / 60)} h`;
}

async function cargarModuloDashboard(vista) {
    const carga = ++dashboardCargaVersion;
    const vistaAsesor = esRol("asesor");
    const vistaAdmin = esRol("administrador");
    if (vistaAsesor && sesionActual?.id) dashboardFiltros.usuarioId = String(sesionActual.id);

    const paramsPeriodo = new URLSearchParams();
    if (!vistaAsesor) {
        paramsPeriodo.set("desde", dashboardFiltros.desde);
        paramsPeriodo.set("hasta", dashboardFiltros.hasta);
    }
    const paramsHoy = new URLSearchParams();
    if (dashboardFiltros.usuarioId) {
        paramsPeriodo.set("usuarioId", dashboardFiltros.usuarioId);
        paramsHoy.set("usuarioId", dashboardFiltros.usuarioId);
    }

    const cacheKey = `${rolActual}:${paramsPeriodo.toString()}:${paramsHoy.toString()}`;
    const cached = dashboardRenderCache.get(cacheKey);
    if (cached && moduloActual === "dashboard") {
        renderDashboard(vista, cached);
    }

    // Todas las consultas empiezan juntas, pero el resumen principal se pinta
    // apenas llega; los paneles secundarios se incorporan después.
    const responsePromise = api(`/api/crm/reportes/resumen?${paramsPeriodo.toString()}&incluirEquipo=false`);
    const equipoPromise = api(`/api/crm/reportes/carga-asesores?${paramsHoy.toString()}`).catch(() => null);
    const hoyPromise = api(`/api/dashboard/hoy${paramsHoy.size ? `?${paramsHoy.toString()}` : ""}`).catch(() => null);
    const adminPromise = vistaAsesor
        ? Promise.resolve(null)
        : api(`/api/dashboard/administracion?${paramsPeriodo.toString()}`).catch(() => null);
    const integracionesPromise = vistaAdmin
        ? api("/api/integraciones/estado").catch(() => null)
        : Promise.resolve(null);
    const usuariosPromise = vistaAsesor ? Promise.resolve([]) : cargarUsuarios().catch(() => []);

    const response = await responsePromise;

    if (!response.ok) {
        throw new Error("Dashboard no disponible");
    }
    const reporte = await response.json();
    reporte.filtrosDashboard = { ...dashboardFiltros };

    if (carga !== dashboardCargaVersion || moduloActual !== "dashboard") {
        return;
    }

    dashboardRenderCache.set(cacheKey, reporte);
    renderDashboard(vista, reporte);

    const [hoyResponse, adminResponse, integracionesResponse, usuarios, equipoResponse] = await Promise.all([
        hoyPromise,
        adminPromise,
        integracionesPromise,
        usuariosPromise,
        equipoPromise
    ]);
    if (equipoResponse?.ok) reporte.cargaAsesores = await equipoResponse.json();
    if (hoyResponse?.ok) {
        reporte.hoy = await hoyResponse.json();
    }
    if (adminResponse?.ok) reporte.administracion = await adminResponse.json();
    reporte.administracionNoDisponible = !vistaAsesor && !adminResponse?.ok;
    if (integracionesResponse?.ok) reporte.integraciones = await integracionesResponse.json();
    reporte.usuariosDashboard = usuarios || [];

    if (carga !== dashboardCargaVersion || moduloActual !== "dashboard") {
        return;
    }

    dashboardRenderCache.set(cacheKey, reporte);
    renderDashboard(vista, reporte);
}

function obtenerCanalesDashboard(reporte) {
    const canalesBase = reporte.canales && reporte.canales.length ? reporte.canales : [
        {
            canal: "WHATSAPP",
            nombre: "WhatsApp",
            conectado: true,
            clientes: reporte.clientes || 0,
            conversaciones: reporte.conversaciones || 0,
            interacciones: reporte.mensajes || 0,
            entrantes: reporte.entrantes || 0,
            salientes: reporte.salientes || 0,
            oportunidades: (reporte.oportunidades?.abiertas || 0) + (reporte.oportunidades?.ganadas || 0) + (reporte.oportunidades?.perdidas || 0),
            montoAbierto: reporte.oportunidades?.montoAbierto || 0,
            montoGanado: reporte.oportunidades?.montoGanado || 0,
            tareasPendientes: reporte.tareas?.pendientes || 0,
            tareasVencidas: reporte.tareas?.vencidas || 0
        }
    ];
    const canalesNecesarios = [
        { canal: "WHATSAPP", nombre: "WhatsApp", clase: "whatsapp" },
        { canal: "INSTAGRAM", nombre: "Instagram", clase: "instagram" },
        { canal: "FACEBOOK", nombre: "Facebook", clase: "facebook" },
        { canal: "TIKTOK", nombre: "TikTok", clase: "tiktok" }
    ];

    return canalesNecesarios.map(canal => {
        const datos = canalesBase.find(item => item.canal === canal.canal) || {};
        return {
            ...canal,
            conectado: Boolean(datos.conectado),
            clientes: Number(datos.clientes || 0),
            conversaciones: Number(datos.conversaciones || 0),
            interacciones: Number(datos.interacciones || 0),
            entrantes: Number(datos.entrantes || 0),
            salientes: Number(datos.salientes || 0),
            oportunidades: Number(datos.oportunidades || 0),
            montoAbierto: Number(datos.montoAbierto || 0),
            montoGanado: Number(datos.montoGanado || 0),
            tareasPendientes: Number(datos.tareasPendientes || 0),
            tareasVencidas: Number(datos.tareasVencidas || 0)
        };
    });
}

function sumarCanalesDashboard(canales) {
    return canales.reduce((total, canal) => ({
        clientes: total.clientes + canal.clientes,
        conversaciones: total.conversaciones + canal.conversaciones,
        interacciones: total.interacciones + canal.interacciones,
        entrantes: total.entrantes + canal.entrantes,
        salientes: total.salientes + canal.salientes,
        oportunidades: total.oportunidades + canal.oportunidades,
        montoAbierto: total.montoAbierto + canal.montoAbierto,
        montoGanado: total.montoGanado + canal.montoGanado,
        tareasPendientes: total.tareasPendientes + canal.tareasPendientes,
        tareasVencidas: total.tareasVencidas + canal.tareasVencidas
    }), {
        clientes: 0,
        conversaciones: 0,
        interacciones: 0,
        entrantes: 0,
        salientes: 0,
        oportunidades: 0,
        montoAbierto: 0,
        montoGanado: 0,
        tareasPendientes: 0,
        tareasVencidas: 0
    });
}

function calcularPorcentaje(parte, total) {
    if (!total) return 0;
    return Math.min(100, Math.round((Number(parte || 0) / Number(total || 0)) * 100));
}

function crearLogoRed(clase) {
    if (String(clase).toLowerCase() === "whatsapp") {
        return '<span class="brand-logo whatsapp"><img src="/images/whatsapp-circle.svg" alt=""></span>';
    }

    // Instagram usa un solo recurso compartido en toda la aplicación. Así el
    // menú, la bandeja, Marketing, Conexiones y los reportes muestran el mismo logo.
    if (clase === "instagram") {
        return '<span class="brand-logo instagram"><img src="/images/instagram-circle.svg" alt=""></span>';
    }

    const logos = {
        all: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="7" cy="7" r="3.1"></circle><circle cx="17" cy="7" r="3.1"></circle><circle cx="7" cy="17" r="3.1"></circle><circle cx="17" cy="17" r="3.1"></circle></svg>',
        facebook: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="12" fill="#1877F2"></circle><path fill="#fff" d="M15.45 12.72l.36-2.34h-2.25V8.86c0-.64.31-1.26 1.31-1.26h1.02v-2s-.93-.16-1.82-.16c-1.86 0-3.07 1.13-3.07 3.16v1.78H8.94v2.34H11v5.66h2.56v-5.66h1.89Z"></path></svg>',
        tiktok: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="12" fill="#000"></circle><path fill="#25F4EE" d="M16.25 8.15a5.43 5.43 0 0 0 3.15 1.01V6.83a3.1 3.1 0 0 1-.67-.07v1.84a5.43 5.43 0 0 1-3.15-1.01v6.58a4.78 4.78 0 0 1-7.46 3.96 4.77 4.77 0 0 0 8.13-3.39V8.15Zm.82-2.3a3.08 3.08 0 0 1-.82-1.8v-.3h-.64a3.1 3.1 0 0 0 1.46 2.1ZM9.38 15.56a2.18 2.18 0 0 1 2.55-3.4V9.77a4.8 4.8 0 0 0-.67-.04v1.86a2.18 2.18 0 0 0-1.88 3.97Z"></path><path fill="#FE2C55" d="M15.58 7.59a5.43 5.43 0 0 0 3.15 1.01V6.76a3.1 3.1 0 0 1-1.66-.91 3.1 3.1 0 0 1-1.46-2.1h-1.69v10.99a2.18 2.18 0 0 1-4.54.82 2.18 2.18 0 0 1 1.88-3.97V9.73a4.77 4.77 0 0 0-3.14 8.4 4.78 4.78 0 0 0 7.46-3.96V7.59Z"></path><path fill="#fff" d="M14.92 7.02a5.43 5.43 0 0 0 3.15 1.01V6.2a3.1 3.1 0 0 1-1.66-.91 3.08 3.08 0 0 1-.82-1.8h-2.33v10.99a2.18 2.18 0 1 1-2-2.15V9.16a4.78 4.78 0 1 0 3.66 4.64V7.02Z"></path></svg>',
        website: '<svg viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.65" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><path d="M2.7 8h18.6M2 12h20M2.7 16h18.6M12 2v20M12 2C8.8 5 7.2 8.3 7.2 12S8.8 19 12 22M12 2c3.2 3 4.8 6.3 4.8 10S15.2 19 12 22"></path></svg>'
    };

    return `<span class="brand-logo ${clase}">${logos[clase] || logos.all}</span>`;
}

function obtenerTrabajoHoyDashboard(reporte) {
    const hoy = reporte.hoy?.hoy || {};
    const futuro = reporte.hoy?.futuro || {};
    return {
        clientes: hoy.clientesNuevos || [],
        mensajes: hoy.mensajesPendientes || [],
        tareas: hoy.tareas || [],
        oportunidades: hoy.oportunidades || [],
        tareasFuturas: futuro.tareas || [],
        totalClientes: Number(hoy.totalClientesNuevos ?? hoy.clientesNuevos?.length ?? 0),
        totalMensajes: Number(hoy.totalMensajesPendientes ?? hoy.mensajesPendientes?.length ?? 0),
        totalTareas: Number(hoy.totalTareas ?? hoy.tareas?.length ?? 0),
        totalOportunidades: Number(hoy.totalOportunidades ?? hoy.oportunidades?.length ?? 0),
        total: Number(hoy.total || 0)
    };
}

function renderAccionOperativa(item) {
    return `
        <button type="button" class="ops-action ${item.urgente ? "danger" : ""}" ${item.conversacionId ? `data-today-conversation="${item.conversacionId}"` : ""}>
            <span>
                <strong>${escapeHtml(item.titulo)}</strong>
                <small>${escapeHtml(item.detalle)}</small>
            </span>
            <em>${escapeHtml(item.meta)}</em>
        </button>`;
}

// Vista administrativa: mantiene la portada diaria, pero añade contexto,
// métricas ejecutivas, excepciones y salud del sistema en una sola pantalla.
function renderDashboard(vista, reporte) {
    const vistaAsesor = esRol("asesor");
    const vistaAdmin = esRol("administrador");
    const canales = obtenerCanalesDashboard(reporte);
    const resumen = sumarCanalesDashboard(canales);
    const trabajo = obtenerTrabajoHoyDashboard(reporte);
    const oportunidades = reporte.oportunidades || {};
    const administracion = reporte.administracion || {};
    const comercial = administracion.comercial || {};
    const atencion = administracion.atencion || {};
    const seguimiento = administracion.seguimiento || {};
    const sistema = administracion.sistema || {};
    const filtros = reporte.filtrosDashboard || dashboardFiltros;
    const usuarios = reporte.usuariosDashboard || [];
    const carga = reporte.cargaAsesores || [];
    const usuarioFiltrado = usuarios.find(item => String(item.id) === String(filtros.usuarioId));
    const abiertas = Number(comercial.oportunidadesAbiertas ?? oportunidades.abiertas ?? resumen.oportunidades ?? 0);
    const ganadas = Number(oportunidades.ganadas || 0);
    const perdidas = Number(oportunidades.perdidas || 0);
    const winRate = Number(comercial.winRate ?? calcularPorcentaje(ganadas, ganadas + perdidas));
    const montoAbierto = Number(comercial.montoAbierto ?? resumen.montoAbierto ?? 0);
    const montoGanado = Number(comercial.montoGanado ?? resumen.montoGanado ?? 0);
    const variacionVentas = comercial.variacionVentas;
    const integraciones = reporte.integraciones || {};
    const canalesIntegracion = integraciones.canales || [];
    const canalesConectados = canalesIntegracion.filter(item => item.connected).length;
    const r2Listo = Boolean(integraciones.r2?.accountId && integraciones.r2?.accessKeyId && integraciones.r2?.secretAccessKey && integraciones.r2?.bucketName && integraciones.r2?.publicBaseUrl);
    const generadoEn = administracion.periodo?.generadoEn || new Date().toISOString();
    const alcance = vistaAsesor ? "Mi cartera" : usuarioFiltrado?.nombre || "Todo el equipo";

    const acciones = [
        ...trabajo.mensajes.map(item => ({
            titulo: item.nombre || "Cliente pendiente",
            detalle: `${item.canal || "Canal"} · ${item.estado || "Sin estado"}`,
            meta: item.fecha ? formatearFecha(item.fecha) : "Responder",
            conversacionId: item.id,
            urgente: true
        })),
        ...trabajo.tareas.map(item => ({
            titulo: item.titulo || "Tarea pendiente",
            detalle: item.cliente || "Sin cliente",
            meta: `${item.vencida ? "Vencida · " : ""}${item.vence ? formatearFecha(item.vence) : "Hoy"}`,
            conversacionId: item.conversacionId,
            urgente: Boolean(item.vencida)
        })),
        ...trabajo.oportunidades.map(item => ({
            titulo: item.titulo || "Oportunidad por mover",
            detalle: `${item.cliente || "Cliente"} · ${item.etapa || "Etapa"}`,
            meta: formatearMoneda(item.monto || 0, item.moneda || "PEN"),
            conversacionId: item.conversacionId,
            urgente: item.etapa === "NEGOCIACION" || item.etapa === "PROPUESTA"
        })),
        ...trabajo.clientes.map(item => ({
            titulo: item.nombre || "Cliente nuevo",
            detalle: item.canal || "Sin canal",
            meta: item.fecha ? formatearFecha(item.fecha) : "Nuevo",
            conversacionId: item.conversacionId,
            urgente: false
        }))
    ].slice(0, 10);

    const etapaRows = (oportunidades.porEtapa || [])
        .filter(item => item.etapa !== "GANADA" && item.etapa !== "PERDIDA")
        .sort((a, b) => Number(b.montoTotal || 0) - Number(a.montoTotal || 0));
    const maxEtapaMonto = Math.max(...etapaRows.map(item => Number(item.montoTotal || 0)), 1);
    const estadosPeriodo = (reporte.porEstado || []).filter(item => !["CERRADO", "PERDIDO", "NO_RESPONDIO"].includes(item.estado));
    const maxEstado = Math.max(...estadosPeriodo.map(item => Number(item.cantidad || 0)), 1);
    const asesorSobrecargado = carga[0];
    const alertasAdmin = [
        ["Sin asignar", formatearNumero(atencion.sinAsignar || 0), "Conversaciones sin responsable", "leads", Number(atencion.sinAsignar || 0) > 0],
        ["Espera máxima", formatearDuracionDashboard(atencion.minutosEsperaMaxima || 0), `${formatearNumero(atencion.pendientesRespuesta || 0)} esperan respuesta`, "inbox", Number(atencion.minutosEsperaMaxima || 0) >= 60],
        ["Ventas estancadas", formatearNumero(comercial.oportunidadesEstancadas || 0), "Sin movimiento por más de 3 días", "ventas", Number(comercial.oportunidadesEstancadas || 0) > 0],
        ["Sin fecha de cierre", formatearNumero(comercial.oportunidadesSinFecha || 0), "Oportunidades sin previsión", "ventas", Number(comercial.oportunidadesSinFecha || 0) > 0]
    ];

    vista.innerHTML = `
        <div class="ops-dashboard">
            <div class="module-heading ops-heading ops-welcome-card">
                <div>
                    <h1>${vistaAsesor ? "Mi trabajo de hoy" : vistaAdmin ? "Centro de control CRM" : "Supervisión comercial"}</h1>
                    <p>${vistaAsesor
                        ? "Prioriza respuestas, seguimientos y oportunidades abiertas."
                        : `${escapeHtml(alcance)} · ${escapeHtml(formatearPeriodoDashboard(filtros.desde, filtros.hasta))} · actualizado ${escapeHtml(formatearFecha(generadoEn))}`}</p>
                </div>
                <div class="heading-actions ops-heading-actions">
                    <button type="button" class="secondary-btn" id="dashboardRefreshButton"><i data-lucide="refresh-cw"></i><span>Actualizar</span></button>
                </div>
            </div>

            <section class="ops-kpi-grid ops-summary-card" aria-label="Resumen de la operación">
                <button type="button" class="ops-summary-stat" data-dashboard-module="inbox">
                    <span class="ops-summary-avatar primary"><i data-lucide="messages-square"></i></span>
                    <span class="ops-summary-copy"><strong>${formatearNumero(trabajo.totalMensajes)}</strong><span>Mensajes por responder</span><small>Entradas sin respuesta humana</small></span>
                </button>
                <button type="button" class="ops-summary-stat" data-dashboard-module="tareas">
                    <span class="ops-summary-avatar warning"><i data-lucide="calendar-range"></i></span>
                    <span class="ops-summary-copy"><strong>${formatearNumero(seguimiento.tareasVencidas ?? resumen.tareasVencidas)}</strong><span>Tareas vencidas</span><small>${formatearNumero(seguimiento.tareasVencenHoy || 0)} vencen hoy</small></span>
                </button>
                <button type="button" class="ops-summary-stat" data-dashboard-module="ventas">
                    <span class="ops-summary-avatar primary"><i data-lucide="badge-dollar-sign"></i></span>
                    <span class="ops-summary-copy"><strong>${formatearMoneda(montoAbierto)}</strong><span>Cartera abierta</span><small>${formatearNumero(abiertas)} oportunidades activas</small></span>
                </button>
                <button type="button" class="ops-summary-stat" data-dashboard-module="ventas">
                    <span class="ops-summary-avatar success"><i data-lucide="check"></i></span>
                    <span class="ops-summary-copy"><strong>${winRate}%</strong><span>${vistaAsesor ? "Mi conversión" : "Win rate del periodo"}</span><small>${formatearMoneda(montoGanado)} ganado en el periodo</small></span>
                </button>
            </section>

            ${!vistaAsesor ? `
                <form id="dashboardFilterForm" class="ops-filter-bar">
                    <label><span>Desde</span><input type="date" name="desde" value="${escapeAttribute(filtros.desde)}" required></label>
                    <label><span>Hasta</span><input type="date" name="hasta" value="${escapeAttribute(filtros.hasta)}" required></label>
                    <label><span>Responsable</span><select name="usuarioId">
                        <option value="">Todo el equipo</option>
                        ${usuarios.map(usuario => `<option value="${usuario.id}" ${String(filtros.usuarioId) === String(usuario.id) ? "selected" : ""}>${escapeHtml(usuario.nombre || usuario.usuario || "Usuario")}</option>`).join("")}
                    </select></label>
                    <div class="ops-filter-actions">
                        <button type="submit" class="ops-filter-button primary"><i data-lucide="search-check"></i><span>Aplicar</span></button>
                        <button type="button" class="ops-filter-button secondary" id="dashboardResetFilters"><i data-lucide="calendar-range"></i><span>Mes actual</span></button>
                        <button type="button" class="ops-filter-button analytics" data-dashboard-module="marketing"><i data-lucide="chart-spline"></i><span>Marketing</span></button>
                    </div>
                </form>
                ${reporte.administracionNoDisponible ? '<div class="ops-load-warning"><strong>Resumen administrativo parcialmente disponible.</strong><span>Se muestran los datos operativos que sí respondieron. Actualiza después de reiniciar el servidor.</span></div>' : ""}` : ""}

            ${!vistaAsesor ? `
                <section class="ops-executive-grid" aria-label="Rendimiento comercial del periodo">
                    <article class="ops-executive-card"><span>Ventas ganadas</span><strong>${formatearMoneda(montoGanado)}</strong><small>${variacionVentas == null ? "Sin base comparable" : `${Number(variacionVentas) >= 0 ? "+" : ""}${variacionVentas}% vs. periodo anterior`}</small></article>
                    <article class="ops-executive-card"><span>Forecast ponderado</span><strong>${formatearMoneda(comercial.forecastPonderado || 0)}</strong><small>Según probabilidad de cierre</small></article>
                    <article class="ops-executive-card ${Number(atencion.sinAsignar || 0) ? "warning" : ""}"><span>Sin asignar</span><strong>${formatearNumero(atencion.sinAsignar || 0)}</strong><small>Necesitan responsable</small></article>
                    <article class="ops-executive-card"><span>Cierres próximos</span><strong>${formatearNumero(comercial.cierresProximos || 0)}</strong><small>Próximos 7 días</small></article>
                </section>` : ""}

            <section class="ops-grid">
                <div class="ops-column">
                    <article class="meta-panel ops-panel ops-priority-panel">
                        <div class="panel-heading-with-action"><div><span class="panel-kicker">Prioridad</span><h2>${vistaAsesor ? "Siguiente acción" : "Trabajo que bloquea avance"}</h2></div><span class="alert-chip">${formatearNumero(trabajo.total)} acciones</span></div>
                        <div class="ops-action-list">${acciones.map(renderAccionOperativa).join("") || '<div class="empty">No hay acciones urgentes.</div>'}</div>
                        ${trabajo.total > acciones.length ? `<p class="ops-footnote">Se muestran las 10 primeras de ${formatearNumero(trabajo.total)} acciones.</p>` : ""}
                    </article>
                    ${vistaAsesor ? (trabajo.tareasFuturas.length ? `
                        <article class="meta-panel ops-panel"><div class="panel-heading-with-action"><div><span class="panel-kicker">Agenda</span><h2>Después de hoy</h2></div><button type="button" class="mini-action" data-dashboard-module="tareas">Ver tareas</button></div><div class="ops-action-list compact">${trabajo.tareasFuturas.slice(0, 6).map(item => renderAccionOperativa({ titulo: item.titulo || "Seguimiento", detalle: item.cliente || "Sin cliente", meta: item.vence ? formatearFecha(item.vence) : "Sin fecha", conversacionId: item.conversacionId, urgente: false })).join("")}</div></article>` : "")
                        : `<article class="meta-panel ops-panel">
                        <div class="panel-heading-with-action"><div><span class="panel-kicker">Equipo</span><h2>Carga por asesor</h2></div>${puedeGestionarEquipoCRM() ? `<button type="button" id="dashboardRebalanceButton" class="mini-action" ${Number(atencion.sinAsignar || 0) === 0 ? "disabled" : ""}>Repartir ${formatearNumero(atencion.sinAsignar || 0)} pendientes</button>` : ""}</div>
                        <div class="ops-team-list">${carga.slice(0, 8).map(item => `<button type="button" class="ops-team-row ${item.tareasVencidas ? "danger" : ""}" data-dashboard-advisor="${item.usuarioId}"><div><strong>${escapeHtml(item.nombre || "Usuario")}</strong><span>${escapeHtml(item.rol || "Asesor")}</span></div><small>${formatearNumero(item.conversacionesActivas || 0)} chats</small><small>${formatearNumero(item.tareasPendientes || 0)} tareas · ${formatearNumero(item.tareasVencidas || 0)} vencidas</small><small>${formatearNumero(item.oportunidadesAbiertas || 0)} ventas</small><b>${formatearNumero(item.cargaTotal || 0)}</b></button>`).join("") || '<div class="empty">Sin asesores activos.</div>'}</div>
                        ${asesorSobrecargado ? `<p class="ops-footnote">Mayor carga: ${escapeHtml(asesorSobrecargado.nombre || "Sin asignación")} con ${formatearNumero(asesorSobrecargado.cargaTotal || 0)} elementos abiertos. Selecciona un asesor para revisar su cartera.</p>` : ""}
                    </article>`}
                </div>
                <div class="ops-column">
                    <article class="meta-panel ops-panel">
                        <div class="panel-heading-with-action"><div><span class="panel-kicker">Ventas</span><h2>Oportunidades por etapa</h2></div><button type="button" class="mini-action" data-dashboard-module="ventas">Abrir ventas</button></div>
                        <div class="ops-bar-list">${etapaRows.map(item => `<div class="ops-bar-row"><div><strong>${escapeHtml(item.etapa)}</strong><span>${formatearNumero(item.cantidad)} oportunidades</span></div><span class="chart-track"><span class="chart-bar whatsapp" style="width:${Math.max(calcularPorcentaje(item.montoTotal, maxEtapaMonto), item.montoTotal > 0 ? 8 : 0)}%"></span></span><em>${formatearMoneda(item.montoTotal || 0)}</em></div>`).join("") || '<div class="empty">Sin oportunidades abiertas.</div>'}</div>
                    </article>
                    <article class="meta-panel ops-panel">
                        <div class="panel-heading-with-action"><div><span class="panel-kicker">Atención</span><h2>Conversaciones por estado</h2></div><span class="alert-chip">${formatearNumero(atencion.conversacionesActivas ?? estadosPeriodo.reduce((total, item) => total + Number(item.cantidad || 0), 0))} activas ahora</span></div>
                        <div class="ops-bar-list">${estadosPeriodo.map(item => `<div class="ops-bar-row"><strong>${escapeHtml(item.estado)}</strong><span class="chart-track"><span class="chart-bar facebook" style="width:${Math.max(calcularPorcentaje(item.cantidad, maxEstado), item.cantidad > 0 ? 8 : 0)}%"></span></span><em>${formatearNumero(item.cantidad)}</em></div>`).join("") || '<div class="empty">Sin conversaciones en el periodo.</div>'}</div>
                    </article>
                </div>
            </section>

            ${!vistaAsesor ? `
                <section class="ops-admin-grid">
                    <article class="meta-panel ops-panel"><div class="panel-heading-with-action"><div><span class="panel-kicker">Excepciones</span><h2>Lo que requiere decisión</h2></div><span class="alert-chip">Ahora</span></div><div class="ops-risk-grid">${alertasAdmin.map(item => `<button type="button" class="ops-risk-card ${item[4] ? "danger" : ""}" data-dashboard-module="${item[3]}"><span>${escapeHtml(item[0])}</span><strong>${item[1]}</strong><small>${escapeHtml(item[2])}</small></button>`).join("")}</div></article>
                    <article class="meta-panel ops-panel"><div class="panel-heading-with-action"><div><span class="panel-kicker">Calidad comercial</span><h2>Motivos de pérdida</h2></div><button type="button" class="mini-action" data-dashboard-module="ventas">Revisar ventas</button></div><div class="ops-loss-list">${(comercial.motivosPerdida || []).map(item => `<div><span><strong>${escapeHtml(item.motivo)}</strong><small>${formatearNumero(item.cantidad)} oportunidades</small></span><b>${formatearMoneda(item.monto || 0)}</b></div>`).join("") || '<div class="empty">No hay oportunidades perdidas en el periodo.</div>'}</div></article>
                </section>` : ""}

            ${vistaAdmin ? `
                <section class="meta-panel ops-panel ops-system-panel">
                    <div class="panel-heading-with-action"><div><span class="panel-kicker">Administración</span><h2>Salud del sistema</h2></div><button type="button" class="mini-action" data-dashboard-module="conexiones">Gestionar conexiones</button></div>
                    <div class="ops-system-grid">
                        <button type="button" data-dashboard-module="conexiones" class="ops-system-card ${canalesConectados < canalesIntegracion.length ? "warning" : "ready"}"><span>Canales</span><strong>${canalesConectados}/${canalesIntegracion.length || 4}</strong><small>Configurados y listos</small></button>
                        <button type="button" data-dashboard-module="conexiones" class="ops-system-card ${r2Listo ? "ready" : "warning"}"><span>Archivos</span><strong>${r2Listo ? "Operativo" : "Incompleto"}</strong><small>Cloudflare R2</small></button>
                        <button type="button" data-dashboard-module="bot" class="ops-system-card ${integraciones.bot?.whatsappAutoReply ? "ready" : "warning"}"><span>Bot WhatsApp</span><strong>${integraciones.bot?.whatsappAutoReply ? "Activo" : "Pausado"}</strong><small>Respuesta automática</small></button>
                        <button type="button" data-dashboard-module="fallos" class="ops-system-card ${Number(sistema.fallosUltimas24Horas || 0) ? "danger" : "ready"}"><span>Fallos 24 h</span><strong>${formatearNumero(sistema.fallosUltimas24Horas || 0)}</strong><small>Mensajes e integraciones</small></button>
                        <button type="button" data-dashboard-module="usuarios" class="ops-system-card ready"><span>Usuarios activos</span><strong>${formatearNumero(usuarios.length)}</strong><small>Administrar accesos y roles</small></button>
                    </div>
                    <div class="ops-channel-strip">${canalesIntegracion.map(item => `<span class="${item.connected ? "ready" : "warning"}">${crearLogoRed(String(item.canal || "").toLowerCase())}<b>${escapeHtml(item.nombre)}</b><small>${item.connected ? "Conectado" : "Pendiente"}</small></span>`).join("")}</div>
                    <div class="ops-admin-shortcuts"><button type="button" class="ghost-button" data-dashboard-module="marketing">Analítica de marketing</button><button type="button" class="ghost-button" data-dashboard-module="actividad">Ver auditoría</button><button type="button" class="ghost-button" data-dashboard-module="reportes">Reportes CRM</button><button type="button" class="ghost-button" data-dashboard-module="usuarios">Gestionar usuarios</button></div>
                </section>` : ""}
        </div>`;

    if (window.lucide) window.lucide.createIcons();
    vista.querySelectorAll("[data-dashboard-module]").forEach(button => button.addEventListener("click", () => abrirModulo(button.dataset.dashboardModule)));
    vista.querySelector("#dashboardRefreshButton")?.addEventListener("click", () => cargarModuloDashboard(vista));

    const filtrosForm = vista.querySelector("#dashboardFilterForm");
    filtrosForm?.addEventListener("submit", async event => {
        event.preventDefault();
        const datos = Object.fromEntries(new FormData(filtrosForm));
        if (datos.desde > datos.hasta) {
            notificar("La fecha desde no puede ser posterior a la fecha hasta.", "warning");
            return;
        }
        dashboardFiltros = { desde: datos.desde, hasta: datos.hasta, usuarioId: datos.usuarioId || "" };
        await cargarModuloDashboard(vista);
    });
    vista.querySelector("#dashboardResetFilters")?.addEventListener("click", async () => {
        dashboardFiltros = crearFiltrosDashboardIniciales();
        await cargarModuloDashboard(vista);
    });
    vista.querySelectorAll("[data-dashboard-advisor]").forEach(button => button.addEventListener("click", async () => {
        dashboardFiltros.usuarioId = button.dataset.dashboardAdvisor || "";
        await cargarModuloDashboard(vista);
    }));
    vista.querySelectorAll("[data-today-conversation]").forEach(elemento => elemento.addEventListener("click", async () => {
        const conversacionId = Number(elemento.dataset.todayConversation);
        if (!conversacionId) return;
        if (typeof abrirDetalleConversacion === "function") await abrirDetalleConversacion(conversacionId, "dashboard");
        else if (typeof abrirConversacionDesdeNotificacion === "function") await abrirConversacionDesdeNotificacion(conversacionId);
    }));

    const rebalanceButton = vista.querySelector("#dashboardRebalanceButton");
    rebalanceButton?.addEventListener("click", async () => {
        const pendientes = Number(atencion.sinAsignar || 0);
        if (!window.confirm(`Se repartirán ${pendientes} conversaciones sin asignar entre los asesores disponibles. ¿Deseas continuar?`)) return;
        rebalanceButton.disabled = true;
        rebalanceButton.textContent = "Repartiendo...";
        try {
            const response = await api("/api/crm/conversaciones/asignar-pendientes", { method: "POST" });
            if (!response.ok) throw new Error("No se pudo repartir");
            const resultado = await response.json();
            await cargarModuloDashboard(vista);
            notificar(resultado.asignadas > 0 ? `Se reasignaron ${resultado.asignadas} conversaciones pendientes.` : "No había conversaciones pendientes por repartir.", "success");
        } catch (error) {
            console.error(error);
            notificar("No se pudo repartir la carga de pendientes.", "error");
            rebalanceButton.disabled = false;
            rebalanceButton.textContent = `Repartir ${formatearNumero(pendientes)} pendientes`;
        }
    });
}
