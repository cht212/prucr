// Módulo frontend del CRM.

        let actividadPagina = 1;
        let actividadEntidad = "";
        async function cargarModuloActividad(vista, entidad = actividadEntidad) {
            if (entidad !== actividadEntidad) actividadPagina = 1;
            actividadEntidad = entidad;
            if (!puedeVerModulo("actividad")) {
                vista.innerHTML = '<div class="error">No tienes permiso para ver la actividad.</div>';
                return;
            }

            const query = `?page=${actividadPagina}&pageSize=${obtenerTamanoPagina("actividad")}${entidad ? `&entidad=${encodeURIComponent(entidad)}` : ""}`;
            const response = await api(`/api/crm/actividad${query}`);
            if (!response.ok) throw new Error("Actividad no disponible");
            const pagina = await response.json();
            const items = pagina.items || [];
            const entidades = ["", "Cliente", "Conversacion", "Oportunidad", "Tarea"];

            vista.innerHTML = `
                <div class="module-heading">
                    <div>
                        <h1>Actividad</h1>
                        <p>Historial de cambios importantes realizados por el equipo.</p>
                    </div>
                </div>
                <section class="activity-workspace">
                    <div class="task-filters">
                        ${entidades.map(item => `
                            <button type="button" class="filter-button ${entidad === item ? "active" : ""}" data-activity-filter="${item}">
                                ${item || "Todo"}
                            </button>`).join("")}
                    </div>
                    <div class="activity-timeline">
                        ${items.map(item => `
                            <article class="activity-item">
                                <div class="activity-dot"></div>
                                <div class="activity-card">
                                    <div class="activity-head">
                                        <strong>${escapeHtml(formatearAccionActividad(item.accion))}</strong>
                                        <span>${formatearFecha(item.fecha)}</span>
                                    </div>
                                    <p>${escapeHtml(item.entidad)} #${escapeHtml(String(item.entidadId || ""))}</p>
                                    <div class="activity-values">
                                        ${item.anterior ? `<span><b>Antes:</b> ${escapeHtml(item.anterior)}</span>` : ""}
                                        ${item.nuevo ? `<span><b>Ahora:</b> ${escapeHtml(item.nuevo)}</span>` : ""}
                                    </div>
                                    <small>${escapeHtml(item.usuario || "sistema")}</small>
                                </div>
                            </article>`).join("") || '<div class="empty">Sin actividad registrada.</div>'}
                    </div>
                    ${renderPaginacion(pagina, "actividad")}
                </section>`;

            enlazarPaginacion(vista, page => { actividadPagina = page; return cargarModuloActividad(vista); });

            vista.querySelectorAll("[data-activity-filter]").forEach(button => {
                button.addEventListener("click", () => cargarModuloActividad(vista, button.dataset.activityFilter));
            });
        }

        function formatearAccionActividad(accion) {
            const valor = (accion || "").toString().trim().toUpperCase();
            const etiquetas = {
                ACTUALIZACION_PERFIL_META: "Perfil Meta actualizado",
                EDICION: "Edición",
                CREACION: "Creación",
                CAMBIO_ESTADO: "Cambio de estado",
                ASIGNACION: "Asignación",
                REASIGNACION: "Reasignación",
                NOTA: "Nota interna",
                TAREA: "Tarea",
                VENTA: "Venta"
            };

            if (etiquetas[valor]) return etiquetas[valor];
            return valor
                ? valor.toLowerCase().replaceAll("_", " ").replace(/^\w/, letra => letra.toUpperCase())
                : "Actividad";
        }

        async function cargarModuloReportes(vista) {
            const puedeExportar = tienePermiso("datos.exportar");
            if (esRol("asesor") && sesionActual?.id && !reportesFiltros.usuarioId) {
                reportesFiltros.usuarioId = String(sesionActual.id);
            }

            const params = new URLSearchParams();
            if (reportesFiltros.desde) params.set("desde", reportesFiltros.desde);
            if (reportesFiltros.hasta) params.set("hasta", reportesFiltros.hasta);
            if (reportesFiltros.usuarioId) params.set("usuarioId", reportesFiltros.usuarioId);
            const query = params.toString();
            const [response, usuarios] = await Promise.all([
                api(`/api/crm/reportes/resumen${query ? `?${query}` : ""}`),
                cargarUsuarios()
            ]);
            if (!response.ok) throw new Error("Reportes no disponibles");
            const reporte = await response.json();
            const tareas = reporte.tareas || {};
            const oportunidades = reporte.oportunidades || {};
            const estados = reporte.porEstado || [];
            const etapas = oportunidades.porEtapa || [];
            const cargaAsesores = reporte.cargaAsesores || [];

            vista.innerHTML = `
                        <div class="module-heading"><div><h1>Reportes</h1><p>Resumen operativo del CRM.</p></div>${puedeExportar ? '<button id="reportsExportButton" class="secondary-btn" type="button">Exportar CSV</button>' : ""}</div>
                        <form id="reportsFilterForm" class="report-filter-form">
                            <label>
                                <span>Desde</span>
                                <input type="date" name="desde" value="${escapeAttribute(reportesFiltros.desde)}">
                            </label>
                            <label>
                                <span>Hasta</span>
                                <input type="date" name="hasta" value="${escapeAttribute(reportesFiltros.hasta)}">
                            </label>
                            <label>
                                <span>Asesor</span>
                                <select name="usuarioId">
                                    <option value="">Todos los asesores</option>
                                    ${usuarios.map(usuario => `
                                        <option value="${usuario.id}" ${String(reportesFiltros.usuarioId) === String(usuario.id) ? "selected" : ""}>
                                            ${escapeHtml(usuario.nombre || usuario.usuario || "Usuario")}
                                        </option>`).join("")}
                                </select>
                            </label>
                            <div class="report-filter-actions">
                                <button type="submit" class="primary">Aplicar</button>
                                <button type="button" id="clearReportsFilter" class="ghost-button">Limpiar</button>
                            </div>
                        </form>
                        <div class="module-grid">
                            <div class="metric-card"><span class="metric-label">Clientes</span><strong class="metric-value">${reporte.clientes}</strong></div>
                            <div class="metric-card"><span class="metric-label">Conversaciones</span><strong class="metric-value">${reporte.conversaciones}</strong></div>
                            <div class="metric-card"><span class="metric-label">Mensajes recibidos</span><strong class="metric-value">${reporte.entrantes}</strong></div>
                            <div class="metric-card"><span class="metric-label">Mensajes enviados</span><strong class="metric-value">${reporte.salientes}</strong></div>
                            <div class="metric-card"><span class="metric-label">Tareas pendientes</span><strong class="metric-value">${tareas.pendientes || 0}</strong></div>
                            <div class="metric-card"><span class="metric-label">Tareas vencidas</span><strong class="metric-value">${tareas.vencidas || 0}</strong></div>
                            <div class="metric-card"><span class="metric-label">Ventas abiertas</span><strong class="metric-value">${formatearMoneda(oportunidades.montoAbierto || 0)}</strong></div>
                            <div class="metric-card"><span class="metric-label">Ventas ganadas</span><strong class="metric-value">${formatearMoneda(oportunidades.montoGanado || 0)}</strong></div>
                        </div>
                        <div class="report-sections">
                            <section class="report-section">
                                <h2>Conversaciones por estado</h2>
                                <table class="module-table"><thead><tr><th>Estado</th><th>Cantidad</th></tr></thead>
                                <tbody>${estados.map(item => `<tr><td>${escapeHtml(item.estado)}</td><td>${item.cantidad}</td></tr>`).join("") || '<tr><td colspan="2">Sin conversaciones.</td></tr>'}</tbody></table>
                            </section>
                            <section class="report-section">
                                <h2>Oportunidades por etapa</h2>
                                <table class="module-table"><thead><tr><th>Etapa</th><th>Cantidad</th><th>Monto</th></tr></thead>
                                <tbody>${etapas.map(item => `<tr><td>${escapeHtml(item.etapa)}</td><td>${item.cantidad}</td><td>${formatearMoneda(item.montoTotal || 0)}</td></tr>`).join("") || '<tr><td colspan="3">Sin oportunidades.</td></tr>'}</tbody></table>
                            </section>
                            <section class="report-section">
                                <h2>Seguimiento</h2>
                                <div class="module-grid compact">
                                    <div class="metric-card"><span class="metric-label">Completadas</span><strong class="metric-value">${tareas.completadas || 0}</strong></div>
                                    <div class="metric-card"><span class="metric-label">Abiertas</span><strong class="metric-value">${oportunidades.abiertas || 0}</strong></div>
                                    <div class="metric-card"><span class="metric-label">Ganadas</span><strong class="metric-value">${oportunidades.ganadas || 0}</strong></div>
                                    <div class="metric-card"><span class="metric-label">Perdidas</span><strong class="metric-value">${oportunidades.perdidas || 0}</strong></div>
                                </div>
                            </section>
                            <section class="report-section">
                                <h2>Carga por asesor</h2>
                                <table class="module-table">
                                    <thead>
                                        <tr>
                                            <th>Asesor</th>
                                            <th>Chats activos</th>
                                            <th>Tareas pendientes</th>
                                            <th>Vencidas</th>
                                            <th>Ventas abiertas</th>
                                            <th>Monto abierto</th>
                                        </tr>
                                    </thead>
                                    <tbody>${cargaAsesores.map(item => `
                                        <tr>
                                            <td><strong>${escapeHtml(item.nombre || "Usuario")}</strong><br><small>${escapeHtml(item.rol || "")}</small></td>
                                            <td>${item.conversacionesActivas || 0}</td>
                                            <td>${item.tareasPendientes || 0}</td>
                                            <td>${item.tareasVencidas || 0}</td>
                                            <td>${item.oportunidadesAbiertas || 0}</td>
                                            <td>${formatearMoneda(item.montoAbierto || 0)}</td>
                                        </tr>`).join("") || '<tr><td colspan="6">Sin asesores activos.</td></tr>'}</tbody>
                                </table>
                            </section>
                        </div>
                        `;

            vista.querySelector("#reportsExportButton")?.addEventListener("click", () => descargarArchivo(`/api/crm/reportes/exportar${query ? `?${query}` : ""}`));

            const form = vista.querySelector("#reportsFilterForm");
            form?.addEventListener("submit", async event => {
                event.preventDefault();
                const datos = Object.fromEntries(new FormData(form));
                reportesFiltros = {
                    desde: datos.desde || "",
                    hasta: datos.hasta || "",
                    usuarioId: datos.usuarioId || ""
                };
                await cargarModuloReportes(vista);
            });

            vista.querySelector("#clearReportsFilter")?.addEventListener("click", async () => {
                reportesFiltros = { desde: "", hasta: "", usuarioId: "" };
                await cargarModuloReportes(vista);
            });
        }

        async function cargarEstadisticasPublicaciones(vista, desde, hasta) {
            const panel = vista.querySelector("#socialPublicationReport");
            if (!panel) return;
            const params = new URLSearchParams();
            if (desde) params.set("desde", desde);
            if (hasta) params.set("hasta", hasta);
            const response = await api(`/api/integraciones/publicaciones/estadisticas?${params}`);
            if (!panel.isConnected) return;
            if (!response.ok) {
                panel.innerHTML = "<h2>Publicaciones de redes</h2><p>No se pudieron cargar las estadísticas.</p>";
                return;
            }
            const data = await response.json();
            const canales = data.canales || [];
            const serie = data.serie || [];
            const publicaciones = canales.flatMap(canal => canal.posts || [])
                .sort((a, b) => String(b.publicadoEn).localeCompare(String(a.publicadoEn)));
            const max = Math.max(1, ...serie.map(item => Number(item.publicaciones || 0)));
            const urlSegura = url => {
                try { return new URL(url).protocol === "https:" ? url : ""; }
                catch { return ""; }
            };
            panel.innerHTML = `
                <h2>Publicaciones de Facebook, Instagram y TikTok</h2>
                <p>Filtrado por fecha de publicación (UTC). Las interacciones son los totales actuales de cada publicación.</p>
                <div class="module-grid compact">
                    ${canales.map(canal => `<div class="metric-card"><span class="metric-label">${escapeHtml(canal.canal)}</span>
                        <strong class="metric-value">${canal.posts?.length || 0}</strong><small>publicaciones</small>
                        ${canal.error ? `<small>${escapeHtml(canal.error)}</small>` : ""}
                        ${canal.partial ? "<small>Hay más publicaciones fuera de este resultado.</small>" : ""}</div>`).join("")}
                </div>
                <h3>Publicaciones por día</h3>
                <div class="social-report-chart">
                    ${serie.map(item => `<div class="social-report-row"><span>${escapeHtml(item.fecha)} · ${escapeHtml(item.canal)}</span>
                        <span class="chart-track"><span class="chart-bar ${escapeAttribute(item.canal.toLowerCase())}" style="width:${Math.round(100 * Number(item.publicaciones || 0) / max)}%"></span></span>
                        <strong>${Number(item.publicaciones || 0)}</strong></div>`).join("") || "<p>No hay publicaciones en el rango.</p>"}
                </div>
                <h3>Detalle de publicaciones</h3>
                <table class="module-table"><thead><tr><th>Fecha</th><th>Red</th><th>Publicación</th><th>Me gusta</th><th>Comentarios</th><th>Compartidos</th><th>Vistas</th></tr></thead>
                    <tbody>${publicaciones.map(item => `<tr><td>${escapeHtml(String(item.publicadoEn || "").slice(0, 10))}</td>
                        <td>${escapeHtml(item.canal)}</td><td>${urlSegura(item.url)
                            ? `<a href="${escapeAttribute(urlSegura(item.url))}" target="_blank" rel="noopener noreferrer">${escapeHtml((item.texto || "Ver publicación").slice(0, 90))}</a>`
                            : escapeHtml((item.texto || item.id || "").slice(0, 90))}</td>
                        <td>${Number(item.meGusta || 0)}</td><td>${Number(item.comentarios || 0)}</td>
                        <td>${Number(item.compartidos || 0)}</td><td>${Number(item.visualizaciones || 0)}</td></tr>`).join("")
                        || '<tr><td colspan="7">Sin publicaciones.</td></tr>'}</tbody></table>`;
        }

        function rangoMarketingPredeterminado(dias = 30) {
            const hasta = new Date();
            const desde = new Date(hasta);
            desde.setDate(desde.getDate() - (dias - 1));
            const iso = fecha => {
                const year = fecha.getFullYear();
                const month = String(fecha.getMonth() + 1).padStart(2, "0");
                const day = String(fecha.getDate()).padStart(2, "0");
                return `${year}-${month}-${day}`;
            };
            return { desde: iso(desde), hasta: iso(hasta) };
        }

        let marketingCargaVersion = 0;
        const marketingComentariosSincronizados = new Set();
        const marketingErroresComentarios = new Map();
        const marketingDatosCache = new Map();

        async function cargarModuloMarketing(vista, opciones = {}) {
            const cargaActual = ++marketingCargaVersion;
            if (!puedeVerModulo("marketing")) {
                vista.innerHTML = '<div class="error">No tienes permiso para consultar Marketing.</div>';
                return;
            }

            if (!marketingFiltros.desde || !marketingFiltros.hasta) {
                marketingFiltros = { ...rangoMarketingPredeterminado(30), canal: marketingFiltros.canal || "TODOS" };
            }

            const canalActivo = String(marketingFiltros.canal || "TODOS").toUpperCase();
            const params = new URLSearchParams({ desde: marketingFiltros.desde, hasta: marketingFiltros.hasta });
            if (opciones.forzarActualizacion) params.set("refresh", "true");
            const comentariosUrl = canalActivo === "TODOS"
                ? "/api/crm/comentarios?pageSize=200"
                : `/api/crm/comentarios?pageSize=200&canal=${encodeURIComponent(canalActivo)}`;
            if (!opciones.silenciosa) {
                vista.innerHTML = `
                    <div class="module-heading marketing-heading"><div><h1>Marketing</h1><p>Las publicaciones y métricas de redes se cargan en segundo plano.</p></div></div>
                    <section class="marketing-loading" role="status" aria-live="polite" aria-busy="true">
                        <div class="marketing-loading-status"><i data-lucide="loader-circle"></i><span>Abriendo Marketing mientras consultamos las redes…</span></div>
                        <div class="marketing-loading-skeleton" aria-hidden="true">
                            <div></div><div></div><div></div><div></div>
                        </div>
                    </section>`;
                if (window.lucide) window.lucide.createIcons();
            }

            const consultarConLimite = async (url, limiteMs = 15000) => {
                const controller = new AbortController();
                const navigationSignal = window.__crmNavigationController?.signal;
                const cancelarNavegacion = () => controller.abort();
                navigationSignal?.addEventListener("abort", cancelarNavegacion, { once: true });
                const timeout = setTimeout(() => controller.abort(), limiteMs);
                try {
                    return await api(url, { signal: controller.signal });
                } catch (error) {
                    if (error?.name !== "AbortError") console.warn(`No se pudo consultar ${url}.`, error);
                    return null;
                } finally {
                    clearTimeout(timeout);
                    navigationSignal?.removeEventListener("abort", cancelarNavegacion);
                }
            };

            const cacheKey = `${marketingFiltros.desde}:${marketingFiltros.hasta}:${canalActivo}`;
            const cached = marketingDatosCache.get(cacheKey);
            let publicacionesData = cached?.publicaciones || { canales: [], serie: [], cargando: true };
            let metaData = cached?.meta || { canales: [], cargando: true };
            let comentariosData = cached?.comentarios || { items: [], total: 0 };
            let primerRender = false;

            function pintarMarketing() {
            const perteneceAlCanal = canal => canalActivo === "TODOS" || String(canal || "").toUpperCase() === canalActivo;
            const canalesPublicacion = (publicacionesData.canales || []).filter(canal => perteneceAlCanal(canal.canal));
            const publicaciones = canalesPublicacion.flatMap(canal => (canal.posts || []).map(item => ({
                ...item,
                canal: item.canal || canal.canal
            })))
                .sort((a, b) => String(b.publicadoEn || "").localeCompare(String(a.publicadoEn || "")));
            const serie = (publicacionesData.serie || []).filter(item => perteneceAlCanal(item.canal));
            let comentarios = (comentariosData.items || []).filter(item => perteneceAlCanal(item.canal));
            const comentariosSincronizados = marketingComentariosSincronizados;
            const erroresComentarios = marketingErroresComentarios;
            const fuenteTikTok = (publicacionesData.canales || []).find(item =>
                String(item.canal || "").toUpperCase() === "TIKTOK");
            const publicacionesTikTok = fuenteTikTok?.posts || [];
            const resumenTikTok = {
                canal: "TIKTOK",
                nombre: "TikTok",
                configurado: Boolean(fuenteTikTok?.configured ?? fuenteTikTok?.configurado),
                estado: publicacionesData.cargando ? "CARGANDO" : publicacionesData.error ? "ERROR" : fuenteTikTok?.error
                    ? "ERROR"
                    : (fuenteTikTok?.configured ?? fuenteTikTok?.configurado) ? "OPERATIVO" : "PENDIENTE",
                mensaje: publicacionesData.error || (publicacionesData.cargando ? "Consultando publicaciones..." : null) || fuenteTikTok?.error || ((fuenteTikTok?.configured ?? fuenteTikTok?.configurado)
                    ? "Métricas de TikTok leídas correctamente desde Display API."
                    : "Configura el token de TikTok Display API con permiso video.list."),
                vistas: publicacionesTikTok.reduce((total, item) => total + Number(item.visualizaciones || 0), 0),
                meGusta: publicacionesTikTok.reduce((total, item) => total + Number(item.meGusta || 0), 0),
                comentarios: publicacionesTikTok.reduce((total, item) => total + Number(item.comentarios || 0), 0),
                compartidos: publicacionesTikTok.reduce((total, item) => total + Number(item.compartidos || 0), 0)
            };
            const crearTarjetaMeta = (canal, nombre) => {
                const fuente = (publicacionesData.canales || []).find(item =>
                    String(item.canal || "").toUpperCase() === canal);
                const insight = (metaData.canales || []).find(item =>
                    String(item.canal || "").toUpperCase() === canal);
                if (insight) return { ...insight, commentsError: fuente?.commentsError };

                const configurado = Boolean(fuente?.configured ?? fuente?.configurado);
                return {
                    canal,
                    nombre,
                    configurado,
                    estado: metaData.cargando ? "CARGANDO" : metaData.error || fuente?.error ? "ERROR" : configurado ? "SIN DATOS" : "PENDIENTE",
                    commentsError: fuente?.commentsError,
                    mensaje: metaData.error || (metaData.cargando ? "Consultando metricas..." : null) || fuente?.error || (configurado
                        ? `No se recibieron estadísticas de ${nombre} en esta consulta.`
                        : `Configura la conexión de ${nombre} para consultar sus estadísticas.`)
                };
            };
            // Estas tarjetas representan el estado general de las tres redes y
            // permanecen visibles aunque el filtro inferior seleccione una sola.
            const tarjetasCanal = [
                crearTarjetaMeta("FACEBOOK", "Facebook"),
                crearTarjetaMeta("INSTAGRAM", "Instagram"),
                resumenTikTok
            ];
            const insightInstagram = (metaData.canales || []).find(item =>
                String(item.canal || "").toUpperCase() === "INSTAGRAM") || {};
            const vistasSeguidores = Number(insightInstagram.vistasSeguidores || 0);
            const vistasNoSeguidores = Number(insightInstagram.vistasNoSeguidores || 0);
            const totalVistasAudiencia = vistasSeguidores + vistasNoSeguidores;
            const porcentajeSeguidores = totalVistasAudiencia
                ? Math.round(vistasSeguidores * 100 / totalVistasAudiencia)
                : 0;
            const edadesAudiencia = Object.entries(insightInstagram.edades || {})
                .map(([rango, cantidad]) => [rango, Number(cantidad || 0)])
                .filter(([, cantidad]) => cantidad > 0)
                .sort((a, b) => String(a[0]).localeCompare(String(b[0])));
            const maxEdadAudiencia = Math.max(1, ...edadesAudiencia.map(([, cantidad]) => cantidad));
            const redesMarketing = [
                { valor: "TODOS", etiqueta: "Todas", logo: "all" },
                { valor: "FACEBOOK", etiqueta: "Facebook", logo: "facebook" },
                { valor: "INSTAGRAM", etiqueta: "Instagram", logo: "instagram" },
                { valor: "TIKTOK", etiqueta: "TikTok", logo: "tiktok" }
            ];
            const diasSeleccionados = Math.round((new Date(`${marketingFiltros.hasta}T00:00:00`) - new Date(`${marketingFiltros.desde}T00:00:00`)) / 86400000) + 1;
            const total = publicaciones.reduce((acumulado, item) => ({
                publicaciones: acumulado.publicaciones + 1,
                meGusta: acumulado.meGusta + Number(item.meGusta || 0),
                comentarios: acumulado.comentarios + Number(item.comentarios || 0),
                compartidos: acumulado.compartidos + Number(item.compartidos || 0),
                visualizaciones: acumulado.visualizaciones + Number(item.visualizaciones ?? 0),
                publicacionesConVistas: acumulado.publicacionesConVistas + (item.visualizaciones == null ? 0 : 1)
            }), { publicaciones: 0, meGusta: 0, comentarios: 0, compartidos: 0, visualizaciones: 0, publicacionesConVistas: 0 });
            const serieInteracciones = serie.map(item => ({
                ...item,
                interacciones: Number(item.meGusta || 0) + Number(item.comentarios || 0) + Number(item.compartidos || 0)
            }));
            const maxInteracciones = Math.max(1, ...serieInteracciones.map(item => item.interacciones));
            const urlSegura = url => {
                try { return new URL(url).protocol === "https:" ? url : ""; }
                catch { return ""; }
            };
            const puedeResponderComentarios = esRol("administrador") || tienePermiso("marketing.gestionar");
            const renderComentarioMarketing = item => {
                const canalComentario = String(item.canal || "").toUpperCase();
                const admiteRespuesta = ["FACEBOOK", "INSTAGRAM"].includes(canalComentario) && item.externalId;
                const respuestas = Array.isArray(item.respuestas) ? item.respuestas : [];
                return `<article class="marketing-comment-item">
                    <header>${crearLogoRed(canalComentario.toLowerCase())}<span><strong>${escapeHtml(item.cliente?.nombre || "Usuario de red")}</strong><small>${escapeHtml(formatearFecha(item.fecha))}</small></span></header>
                    <p>${escapeHtml(String(item.texto || "").replace(/^Comentario:\s*/i, ""))}</p>
                    ${respuestas.map(respuesta => `<div class="marketing-comment-answer"><strong>Respuesta de Marketing</strong><span>${escapeHtml(respuesta.texto || "")}</span><small>${escapeHtml(formatearFecha(respuesta.fecha))}</small></div>`).join("")}
                    ${puedeResponderComentarios && admiteRespuesta ? `<form class="marketing-comment-reply" data-marketing-comment-reply="${Number(item.id)}">
                        <textarea name="texto" maxlength="1000" rows="2" placeholder="Escribe una respuesta pública…" aria-label="Respuesta pública" required></textarea>
                        <button type="submit"><i data-lucide="reply"></i><span>Responder públicamente</span></button>
                    </form>` : !admiteRespuesta ? '<small class="marketing-comment-help">Esta red no permite responder este comentario desde el CRM.</small>' : ""}
                </article>`;
            };

            const mostrarDetallePublicacion = item => {
                const canal = String(item.canal || "").toUpperCase();
                const clavePublicacion = `${canal}:${String(item.id || "")}`;
                const canalLogo = canal.toLowerCase();
                const imagen = urlSegura(item.imagenUrl);
                const enlace = urlSegura(item.url);
                const tipo = String(item.tipo || "PUBLICACION").replaceAll("_", " ");
                const comentariosDePublicacion = comentarios.filter(comentario =>
                    String(comentario.publicacionId || "") === String(item.id || ""));
                const fuentePublicacion = canalesPublicacion.find(canalPublicacion =>
                    String(canalPublicacion.canal || "").toUpperCase() === canal);
                const errorComentarios = erroresComentarios.get(clavePublicacion) || fuentePublicacion?.commentsError;
                const meGusta = Number(item.meGusta || 0);
                const cantidadComentarios = Number(item.comentarios || 0);
                const compartidos = Number(item.compartidos || 0);
                const visualizaciones = item.visualizaciones == null ? null : Number(item.visualizaciones || 0);
                const interacciones = meGusta + cantidadComentarios + compartidos;
                const reaccionesPorTipo = { ...(item.reacciones || {}) };
                if (canal === "FACEBOOK" && !Object.values(reaccionesPorTipo).some(Number) && meGusta > 0) {
                    reaccionesPorTipo.like = meGusta;
                }
                const tiposReaccion = canal === "FACEBOOK"
                    ? [
                        ["like", "👍", "Me gusta"],
                        ["love", "❤️", "Me encanta"],
                        ["care", "🥰", "Me importa"],
                        ["haha", "😆", "Me divierte"],
                        ["wow", "😮", "Me asombra"],
                        ["sorry", "😢", "Me entristece"],
                        ["anger", "😡", "Me enfada"]
                    ]
                    : [["like", "❤️", "Me gusta"]];

                vista.innerHTML = `
                    <div class="marketing-detail-heading">
                        <button type="button" class="marketing-detail-back" data-marketing-detail-back><i data-lucide="arrow-left"></i><span>Volver a publicaciones</span></button>
                        <div><span class="panel-kicker">Biblioteca de contenido</span><h1>Insights de la publicación</h1><p>${escapeHtml(canal)} · ${escapeHtml(tipo)} · ${escapeHtml(String(item.publicadoEn || "").slice(0, 10) || "Sin fecha")}</p></div>
                        ${enlace ? `<a class="secondary-btn" href="${escapeAttribute(enlace)}" target="_blank" rel="noopener noreferrer"><span>Ver original</span><i data-lucide="external-link"></i></a>` : ""}
                    </div>
                    <section class="marketing-publication-detail">
                        <aside class="marketing-detail-preview">
                            <div class="marketing-detail-media">${imagen
                                ? `<img src="${escapeAttribute(imagen)}" alt="Vista de la publicación">`
                                : `<span>${crearLogoRed(canalLogo)}<small>Vista previa no disponible</small></span>`}
                                <div class="marketing-publication-badge">${crearLogoRed(canalLogo)}<span>${escapeHtml(canal)} · ${escapeHtml(tipo)}</span></div>
                            </div>
                            <div class="marketing-detail-copy">
                                <p>${escapeHtml(item.texto || "Publicación sin texto")}</p>
                                <div class="marketing-detail-inline-metrics">
                                    <span><i data-lucide="heart"></i>${formatearNumero(meGusta)}</span>
                                    <span><i data-lucide="message-circle"></i>${formatearNumero(cantidadComentarios)}</span>
                                    <span><i data-lucide="share-2"></i>${formatearNumero(compartidos)}</span>
                                    <span><i data-lucide="eye"></i>${visualizaciones == null ? "—" : formatearNumero(visualizaciones)}</span>
                                </div>
                            </div>
                            <section class="marketing-detail-comments">
                                <div class="marketing-detail-section-title"><div><span class="panel-kicker">Comunidad</span><h2>Comentarios</h2></div><span class="alert-chip">${formatearNumero(comentariosDePublicacion.length)}</span></div>
                                <div class="marketing-comments-list">${comentariosDePublicacion.map(renderComentarioMarketing).join("") || (errorComentarios
                                    ? `<div class="marketing-comment-sync-error"><i data-lucide="shield-alert"></i><span>${escapeHtml(errorComentarios)}</span><button type="button" data-marketing-comment-sync>Volver a intentar</button>${puedeVerModulo("conexiones") ? '<button type="button" data-marketing-connections>Revisar conexión</button>' : ""}</div>`
                                    : '<div class="empty">Esta publicación todavía no tiene comentarios recibidos en el CRM.</div>')}</div>
                            </section>
                        </aside>
                        <div class="marketing-detail-insights">
                            <article class="marketing-insight-card marketing-insight-overview">
                                <div class="marketing-detail-section-title"><div><span class="panel-kicker">Resultado</span><h2>Rendimiento de la publicación</h2></div>${crearLogoRed(canalLogo)}</div>
                                <div class="marketing-detail-kpis">
                                    <span class="${visualizaciones == null ? "unavailable" : ""}"><i data-lucide="eye"></i><strong>${visualizaciones == null ? "—" : formatearNumero(visualizaciones)}</strong><small>Visualizaciones</small></span>
                                    <span><i data-lucide="mouse-pointer-click"></i><strong>${formatearNumero(interacciones)}</strong><small>Interacciones</small></span>
                                    <span><i data-lucide="heart"></i><strong>${formatearNumero(meGusta)}</strong><small>${canal === "FACEBOOK" ? "Reacciones" : "Me gusta"}</small></span>
                                    <span><i data-lucide="message-circle"></i><strong>${formatearNumero(cantidadComentarios)}</strong><small>Comentarios</small></span>
                                    <span><i data-lucide="share-2"></i><strong>${formatearNumero(compartidos)}</strong><small>Compartidos</small></span>
                                </div>
                            </article>
                            <article class="marketing-insight-card marketing-reactions-card">
                                <div class="marketing-detail-section-title"><div><span class="panel-kicker">Reacciones</span><h2>${canal === "FACEBOOK" ? "Reacción por tipo" : "Me gusta de la publicación"}</h2></div><strong>${formatearNumero(meGusta)}</strong></div>
                                <div class="marketing-reaction-types">
                                    ${tiposReaccion.map(([clave, emoji, etiqueta]) => `<div><span aria-hidden="true">${emoji}</span><strong>${formatearNumero(Number(reaccionesPorTipo[clave] || (canal !== "FACEBOOK" && clave === "like" ? meGusta : 0)))}</strong><small>${etiqueta}</small></div>`).join("")}
                                </div>
                            </article>
                            ${canal === "INSTAGRAM" ? `
                                <article class="marketing-insight-card">
                                    <div class="marketing-detail-section-title"><div><span class="panel-kicker">Audiencia del periodo · Instagram</span><h2>Seguidores frente a no seguidores</h2></div><i data-lucide="pie-chart"></i></div>
                                    ${totalVistasAudiencia ? `<div class="marketing-follow-split">
                                        <div class="marketing-follow-donut" style="--followers:${porcentajeSeguidores}%"><strong>${formatearNumero(totalVistasAudiencia)}</strong><small>vistas</small></div>
                                        <div><span><i class="followers"></i>Seguidores<strong>${porcentajeSeguidores}% · ${formatearNumero(vistasSeguidores)}</strong></span><span><i class="non-followers"></i>No seguidores<strong>${100 - porcentajeSeguidores}% · ${formatearNumero(vistasNoSeguidores)}</strong></span></div>
                                    </div>` : '<div class="marketing-audience-empty"><i data-lucide="users"></i><strong>Sin muestra en el periodo</strong><p>Instagram todavía no entregó vistas clasificadas entre seguidores y no seguidores para las fechas seleccionadas.</p></div>'}
                                </article>
                                <article class="marketing-insight-card">
                                    <div class="marketing-detail-section-title"><div><span class="panel-kicker">Audiencia del periodo · Instagram</span><h2>Edad de los seguidores</h2></div><i data-lucide="bar-chart-3"></i></div>
                                    ${edadesAudiencia.length ? `<div class="marketing-age-bars">${edadesAudiencia.map(([rango, cantidad]) => `<div><span>${escapeHtml(rango)}</span><b><i style="width:${Math.round(cantidad * 100 / maxEdadAudiencia)}%"></i></b><strong>${formatearNumero(cantidad)}</strong></div>`).join("")}</div>` : '<div class="marketing-audience-empty"><i data-lucide="shield-check"></i><strong>Aún no hay una muestra suficiente</strong><p>Meta protege estos datos y normalmente los habilita cuando la cuenta profesional supera 100 seguidores y registra actividad reciente.</p></div>'}
                                </article>` : `
                                <article class="marketing-insight-card marketing-insight-unavailable">
                                    <div class="marketing-detail-section-title"><div><span class="panel-kicker">Audiencia</span><h2>Seguidores frente a no seguidores</h2></div><i data-lucide="users"></i></div>
                                    <div><i data-lucide="pie-chart"></i><strong>No disponible para esta publicación</strong><p>${canal === "FACEBOOK" ? "Facebook no entregó este desglose para la publicación mediante la API conectada." : "TikTok no entregó este desglose para la publicación mediante la API conectada."}</p></div>
                                </article>
                                <article class="marketing-insight-card marketing-insight-unavailable">
                                    <div class="marketing-detail-section-title"><div><span class="panel-kicker">Demografía</span><h2>Edad de la audiencia</h2></div><i data-lucide="bar-chart-3"></i></div>
                                    <div><i data-lucide="shield-check"></i><strong>No disponible para esta publicación</strong><p>La red social no entregó una muestra demográfica para este contenido mediante la API conectada.</p></div>
                                </article>`}
                        </div>
                    </section>`;

                if (window.lucide) window.lucide.createIcons();
                vista.querySelector("[data-marketing-detail-back]")?.addEventListener("click", () => cargarModuloMarketing(vista));
                vista.querySelectorAll("[data-marketing-connections]").forEach(button =>
                    button.addEventListener("click", () => abrirModulo("conexiones")));
                vista.querySelector("[data-marketing-comment-sync]")?.addEventListener("click", () => {
                    comentariosSincronizados.delete(clavePublicacion);
                    erroresComentarios.delete(clavePublicacion);
                    mostrarDetallePublicacion(item);
                });
                vista.querySelectorAll("[data-marketing-comment-reply]").forEach(form => form.addEventListener("submit", async event => {
                    event.preventDefault();
                    const comentarioId = Number(form.dataset.marketingCommentReply);
                    const textarea = form.querySelector("textarea[name='texto']");
                    const texto = textarea?.value.trim() || "";
                    const button = form.querySelector("button[type='submit']");
                    if (!comentarioId || !texto) return;
                    button.disabled = true;
                    try {
                        const response = await api(`/api/crm/comentarios/${comentarioId}/respuestas`, {
                            method: "POST",
                            headers: { "Content-Type": "application/json" },
                            body: JSON.stringify({ texto })
                        });
                        if (!response.ok) {
                            const error = await response.json().catch(() => ({}));
                            throw new Error(error.message || "No se pudo publicar la respuesta.");
                        }
                        const respuesta = await response.json();
                        const comentario = comentarios.find(actual => Number(actual.id) === comentarioId);
                        if (comentario) {
                            comentario.respuestas = [...(comentario.respuestas || []), respuesta];
                        }
                        notificar("Respuesta publicada en la red social.", "success");
                        mostrarDetallePublicacion(item);
                    } catch (error) {
                        notificar(error.message || "No se pudo publicar la respuesta.", "error");
                        button.disabled = false;
                    }
                }));

                if (cantidadComentarios > 0 && comentariosDePublicacion.length === 0 &&
                    ["FACEBOOK", "INSTAGRAM"].includes(canal) && item.id &&
                    !comentariosSincronizados.has(clavePublicacion)) {
                    comentariosSincronizados.add(clavePublicacion);
                    const listaComentarios = vista.querySelector(".marketing-comments-list");
                    if (listaComentarios) {
                        listaComentarios.innerHTML = '<div class="empty">Sincronizando comentarios con Instagram…</div>';
                    }

                    (async () => {
                        try {
                            const sincronizacion = await api(
                                `/api/integraciones/publicaciones/${encodeURIComponent(canal)}/${encodeURIComponent(item.id)}/comentarios/sincronizar`,
                                { method: "POST" });
                            const resultado = await sincronizacion.json().catch(() => ({}));
                            if (!sincronizacion.ok) {
                                throw new Error(resultado.message || "Instagram no permitió leer los comentarios.");
                            }
                            if (Number(resultado.count || 0) === 0) {
                                throw new Error("Instagram reporta comentarios, pero todavía no entregó su contenido mediante la API.");
                            }

                            const actualizados = await api(comentariosUrl);
                            if (!actualizados.ok) throw new Error("No se pudo releer los comentarios guardados.");
                            const datosActualizados = await actualizados.json();
                            comentarios = (datosActualizados.items || []).filter(comentario => perteneceAlCanal(comentario.canal));
                            erroresComentarios.delete(clavePublicacion);
                        } catch (error) {
                            if (error?.name === "AbortError") return;
                            erroresComentarios.set(clavePublicacion, error.message || "No se pudo sincronizar el comentario.");
                        }

                        if (moduloActual === "marketing") mostrarDetallePublicacion(item);
                    })();
                }
            };

            // Evita que una respuesta lenta sobrescriba un filtro o una navegación
            // más reciente mientras Marketing se actualiza silenciosamente.
            if (cargaActual !== marketingCargaVersion || moduloActual !== "marketing") return;
            const formAnterior = vista.querySelector("#marketingFilterForm");
            const borrador = formAnterior ? Object.fromEntries(new FormData(formAnterior)) : null;
            const foco = formAnterior?.contains(document.activeElement) ? document.activeElement.name : null;
            const erroresPublicaciones = canalesPublicacion.map(c => c.error || c.commentsError).filter(Boolean);
            const errorCarga = [publicacionesData.error, metaData.error, comentariosData.error, ...erroresPublicaciones].filter(Boolean).join(" ");
            const sinDatosPublicaciones = canalesPublicacion.length > 0 && canalesPublicacion.every(c => c.error) && !publicaciones.length;
            const cifra = valor => publicacionesData.cargando || sinDatosPublicaciones || (publicacionesData.error && !cached?.publicaciones)
                ? "—" : formatearNumero(valor);
            const publicacionesVacias = publicacionesData.cargando ? "Consultando publicaciones..."
                : publicacionesData.error || sinDatosPublicaciones ? "Las publicaciones no estan disponibles en esta consulta."
                : "No hay publicaciones de esta red en el periodo seleccionado.";

            vista.innerHTML = `
                <div class="module-heading marketing-heading">
                    <div><h1>Marketing</h1><p>Rendimiento de redes, publicaciones y conversaciones generadas por la audiencia.</p></div>
                    ${puedeVerModulo("conexiones") ? '<button type="button" class="secondary-btn" data-marketing-connections><i data-lucide="plug-zap"></i><span>Revisar conexiones</span></button>' : ""}
                </div>
                ${errorCarga ? `<div class="marketing-load-warning" role="status"><span>${escapeHtml(errorCarga)}</span><button type="button" class="secondary-btn" data-marketing-retry><i data-lucide="refresh-cw"></i><span>Volver a intentar</span></button></div>` : ""}

                <section class="marketing-network-picker" aria-labelledby="marketingNetworkTitle">
                    <div><span class="panel-kicker">Canal</span><h2 id="marketingNetworkTitle">Red social a evaluar</h2></div>
                    <div class="marketing-network-options" role="group" aria-label="Seleccionar red social">
                        ${redesMarketing.map(red => `<button type="button" data-marketing-channel="${red.valor}" class="${canalActivo === red.valor ? "active" : ""}" aria-pressed="${canalActivo === red.valor}">${crearLogoRed(red.logo)}<span>${red.etiqueta}</span></button>`).join("")}
                    </div>
                </section>

                <form id="marketingFilterForm" class="marketing-filter-bar">
                    <div class="marketing-period-presets" aria-label="Periodos rápidos">
                        <button type="button" data-marketing-days="7" class="${diasSeleccionados === 7 ? "active" : ""}">7 días</button>
                        <button type="button" data-marketing-days="30" class="${diasSeleccionados === 30 ? "active" : ""}">30 días</button>
                        <button type="button" data-marketing-days="90" class="${diasSeleccionados === 90 ? "active" : ""}">90 días</button>
                    </div>
                    <label><span>Desde</span><input type="date" name="desde" value="${escapeAttribute(marketingFiltros.desde)}" required></label>
                    <label><span>Hasta</span><input type="date" name="hasta" value="${escapeAttribute(marketingFiltros.hasta)}" required></label>
                    <button type="submit" class="marketing-apply-button"><i data-lucide="refresh-cw"></i><span>Actualizar datos</span></button>
                </form>

                <section class="marketing-kpis" aria-label="Resumen de publicaciones">
                    <article><i data-lucide="panels-top-left"></i><span>Publicaciones</span><strong>${cifra(total.publicaciones)}</strong></article>
                    <article><i data-lucide="heart"></i><span>Me gusta</span><strong>${cifra(total.meGusta)}</strong></article>
                    <article><i data-lucide="message-circle"></i><span>Comentarios</span><strong>${cifra(total.comentarios)}</strong></article>
                    <article><i data-lucide="share-2"></i><span>Compartidos</span><strong>${cifra(total.compartidos)}</strong></article>
                    <article><i data-lucide="eye"></i><span>Vistas disponibles</span><strong>${total.publicacionesConVistas ? formatearNumero(total.visualizaciones) : "—"}</strong></article>
                </section>

                <section class="marketing-channel-grid">
                    ${tarjetasCanal.map(canal => `
                        <article class="marketing-channel-card ${canal.estado === "ERROR" ? "danger" : canal.commentsError ? "warning" : canal.configurado ? "ready" : "warning"}">
                            <div class="marketing-channel-head">${crearLogoRed(String(canal.canal || "").toLowerCase())}<div><h2>${escapeHtml(canal.nombre || canal.canal)}</h2><span>${escapeHtml(canal.commentsError ? "PERMISO PENDIENTE" : canal.estado || "Sin datos")}</span></div></div>
                            <div class="marketing-channel-metrics" ${["ERROR", "CARGANDO"].includes(canal.estado) ? 'hidden' : ""}>
                                ${String(canal.canal).toUpperCase() === "TIKTOK"
                                    ? `<span><small>Vistas</small><strong>${formatearNumero(canal.vistas || 0)}</strong></span><span><small>Me gusta</small><strong>${formatearNumero(canal.meGusta || 0)}</strong></span><span><small>Comentarios</small><strong>${formatearNumero(canal.comentarios || 0)}</strong></span><span><small>Compartidos</small><strong>${formatearNumero(canal.compartidos || 0)}</strong></span>`
                                    : `<span><small>Alcance</small><strong>${formatearNumero(canal.alcance || 0)}</strong></span>
                                        <span><small>Impresiones</small><strong>${formatearNumero(canal.impresiones || 0)}</strong></span>
                                        <span><small>Interacciones</small><strong>${formatearNumero(canal.interacciones || 0)}</strong></span>
                                        ${String(canal.canal).toUpperCase() === "INSTAGRAM"
                                            ? `<span><small>Visitas al perfil</small><strong>${formatearNumero(canal.visitasPerfil || 0)}</strong></span><span><small>Clics al sitio</small><strong>${formatearNumero(canal.clicks || 0)}</strong></span>`
                                            : `<span><small>Seguidores</small><strong>${formatearNumero(canal.seguidores || 0)}</strong></span>`}`}
                            </div>
                            <p>${escapeHtml(canal.commentsError || canal.mensaje || "")}</p>
                        </article>`).join("") || '<article class="marketing-channel-card warning"><h2>Insights de Meta</h2><p>No hay métricas disponibles. Revisa las conexiones y permisos.</p></article>'}
                </section>

                <section class="marketing-layout marketing-layout-single">
                    <article class="meta-panel">
                        <div class="panel-heading-with-action"><div><span class="panel-kicker">Tendencia</span><h2>Interacciones por día</h2></div><small>Me gusta + comentarios + compartidos</small></div>
                        <div class="marketing-chart">
                            ${serieInteracciones.map(item => `<div class="marketing-chart-row"><span>${escapeHtml(String(item.fecha))}</span><b>${escapeHtml(item.canal)}</b><span class="marketing-chart-track"><i style="width:${Math.max(3, Math.round(item.interacciones * 100 / maxInteracciones))}%"></i></span><strong>${formatearNumero(item.interacciones)}</strong></div>`).join("") || '<div class="empty">No hay interacciones para graficar en este periodo.</div>'}
                        </div>
                    </article>
                </section>

                <section class="meta-panel marketing-publications-panel">
                    <div class="panel-heading-with-action"><div><span class="panel-kicker">Contenido</span><h2>Publicaciones y rendimiento</h2><p>Revisa la pieza publicada junto con sus resultados.</p></div><span class="alert-chip">${cifra(publicaciones.length)} resultados</span></div>
                    <div class="marketing-publication-grid">${publicaciones.map((item, index) => {
                        const imagen = urlSegura(item.imagenUrl);
                        const enlace = urlSegura(item.url);
                        const canal = String(item.canal || "").toLowerCase();
                        const tipo = String(item.tipo || "PUBLICACION").replaceAll("_", " ");
                        const contenido = escapeHtml((item.texto || item.id || "Publicación sin texto").slice(0, 280));
                        return `<article class="marketing-publication-card" data-marketing-publication-card="${index}">
                            <div class="marketing-publication-media">${imagen ? `<img src="${escapeAttribute(imagen)}" alt="Vista de la publicación" loading="lazy">` : `<span>${crearLogoRed(canal)}<small>Vista previa no disponible</small></span>`}<div class="marketing-publication-badge">${crearLogoRed(canal)}<span>${escapeHtml(item.canal || "Red social")} · ${escapeHtml(tipo)}</span></div></div>
                            <div class="marketing-publication-body"><time>${escapeHtml(String(item.publicadoEn || "").slice(0, 10) || "Sin fecha")}</time><p>${contenido}</p>
                                <div class="marketing-publication-metrics"><span><i data-lucide="heart"></i><b>${formatearNumero(item.meGusta || 0)}</b><small>Me gusta</small></span><span><i data-lucide="message-circle"></i><b>${formatearNumero(item.comentarios || 0)}</b><small>Comentarios</small></span><span><i data-lucide="share-2"></i><b>${formatearNumero(item.compartidos || 0)}</b><small>Compartidos</small></span><span class="${item.visualizaciones == null ? "unavailable" : ""}"><i data-lucide="eye"></i><b>${item.visualizaciones == null ? "—" : formatearNumero(item.visualizaciones)}</b><small>${item.visualizaciones == null ? "No disponible" : "Vistas"}</small></span></div>
                                <div class="marketing-publication-actions"><button type="button" data-marketing-publication="${index}"><span>Ver insights y comentarios</span><i data-lucide="arrow-right"></i></button>${enlace ? `<a href="${escapeAttribute(enlace)}" target="_blank" rel="noopener noreferrer" aria-label="Abrir publicación original"><i data-lucide="external-link"></i></a>` : ""}</div>
                            </div>
                        </article>`;
                    }).join("") || `<div class="empty marketing-publications-empty" role="status">${publicacionesVacias}</div>`}</div>
                </section>

                <p class="marketing-data-note">Las cifras dependen de los permisos aprobados por cada red. TikTok entrega vistas, me gusta, comentarios y compartidos por video; los clics y ciertos insights solo aparecerán cuando la API de la cuenta los autorice.</p>`;

            if (window.lucide) window.lucide.createIcons();
            primerRender = true;
            const formActual = vista.querySelector("#marketingFilterForm");
            if (borrador) Object.entries(borrador).forEach(([name, value]) => {
                if (formActual.elements[name]) formActual.elements[name].value = value;
            });
            if (foco) formActual.elements[foco]?.focus();
            vista.querySelector("[data-marketing-retry]")?.addEventListener("click", () =>
                cargarModuloMarketing(vista, { forzarActualizacion: true }));
            vista.querySelectorAll("[data-marketing-connections]").forEach(button =>
                button.addEventListener("click", () => abrirModulo("conexiones")));
            vista.querySelector("#marketingFilterForm")?.addEventListener("submit", async event => {
                event.preventDefault();
                const datos = Object.fromEntries(new FormData(event.currentTarget));
                if (datos.desde > datos.hasta) {
                    notificar("La fecha desde no puede ser posterior a la fecha hasta.", "warning");
                    return;
                }
                marketingFiltros = { ...marketingFiltros, desde: datos.desde, hasta: datos.hasta };
                await cargarModuloMarketing(vista, { forzarActualizacion: true });
            });
            vista.querySelectorAll("[data-marketing-days]").forEach(button => button.addEventListener("click", async () => {
                marketingFiltros = { ...rangoMarketingPredeterminado(Number(button.dataset.marketingDays || 30)), canal: canalActivo };
                await cargarModuloMarketing(vista);
            }));
            vista.querySelectorAll("[data-marketing-channel]").forEach(button => button.addEventListener("click", async () => {
                marketingFiltros = { ...marketingFiltros, canal: button.dataset.marketingChannel || "TODOS" };
                await cargarModuloMarketing(vista);
            }));
            vista.querySelectorAll("[data-marketing-publication]").forEach(button => button.addEventListener("click", () => {
                const item = publicaciones[Number(button.dataset.marketingPublication)];
                if (item) mostrarDetallePublicacion(item);
            }));
            vista.querySelectorAll("[data-marketing-publication-card]").forEach(card => card.addEventListener("click", event => {
                if (event.target.closest("a, button")) return;
                const item = publicaciones[Number(card.dataset.marketingPublicationCard)];
                if (item) mostrarDetallePublicacion(item);
            }));
            }

            void (async () => {
                try {
                    pintarMarketing();
                    const consultarFuente = async (fuente, url, limite) => {
                        const response = await consultarConLimite(url, limite);
                        const anterior = fuente === "publicaciones" ? publicacionesData : fuente === "meta" ? metaData : comentariosData;
                        let data;
                        try {
                            data = response?.ok ? await response.json() : {
                                ...anterior, cargando: false,
                                error: `No se pudieron consultar ${fuente === "meta" ? "las metricas de Meta" : fuente}. Intenta actualizar.`
                            };
                        } catch {
                            data = { ...anterior, cargando: false, error: "La red devolvio una respuesta no valida." };
                        }
                        if (cargaActual !== marketingCargaVersion || moduloActual !== "marketing") return;
                        if (fuente === "publicaciones") publicacionesData = data;
                        else if (fuente === "meta") metaData = data;
                        else comentariosData = data;
                        const anteriorCache = marketingDatosCache.get(cacheKey) || {};
                        if (!data.error) marketingDatosCache.set(cacheKey, { ...anteriorCache, [fuente]: data });
                        if (marketingDatosCache.size > 20) marketingDatosCache.delete(marketingDatosCache.keys().next().value);
                        if (fuente !== "comentarios" || primerRender) pintarMarketing();
                        if (fuente === "publicaciones" && response?.ok && response.headers?.get("X-CRM-Cache") !== "HIT")
                            await consultarFuente("comentarios", comentariosUrl, 8000);
                    };
                    await Promise.all([
                        consultarFuente("publicaciones", `/api/integraciones/publicaciones/estadisticas?${params}`, 15000),
                        consultarFuente("meta", `/api/integraciones/meta/estadisticas?${params}`, 15000),
                        consultarFuente("comentarios", comentariosUrl, 8000)
                    ]);
                } catch (error) {
                    if (error?.name === "AbortError") return;
                    console.error("No se pudo actualizar Marketing.", error);
                    if (cargaActual !== marketingCargaVersion || moduloActual !== "marketing") return;
                    vista.innerHTML = `
                        <div class="module-heading marketing-heading">
                            <div><h1>Marketing</h1><p>El apartado ya esta disponible, pero las redes no respondieron a tiempo.</p></div>
                            ${puedeVerModulo("conexiones") ? '<button type="button" class="secondary-btn" data-marketing-connections><i data-lucide="plug-zap"></i><span>Revisar conexiones</span></button>' : ""}
                        </div>
                        <section class="marketing-loading marketing-loading-error" role="status">
                            <div class="marketing-loading-status"><i data-lucide="wifi-off"></i><span>No se pudieron cargar las metricas de redes en este intento.</span></div>
                            <button type="button" class="marketing-apply-button" data-marketing-retry><i data-lucide="refresh-cw"></i><span>Volver a intentar</span></button>
                        </section>`;
                    if (window.lucide) window.lucide.createIcons();
                    vista.querySelector("[data-marketing-retry]")?.addEventListener("click", () =>
                        cargarModuloMarketing(vista, { forzarActualizacion: true }));
                    vista.querySelectorAll("[data-marketing-connections]").forEach(button =>
                        button.addEventListener("click", () => abrirModulo("conexiones")));
                }
            })();
        }
