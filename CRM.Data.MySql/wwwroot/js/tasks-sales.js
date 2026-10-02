// Módulo frontend del CRM.

        function esMismaFecha(fecha, referencia) {
            if (!fecha) return false;
            const actual = new Date(fecha);
            return actual.getFullYear() === referencia.getFullYear() &&
                actual.getMonth() === referencia.getMonth() &&
                actual.getDate() === referencia.getDate();
        }

        function obtenerResumenTareas(tareas) {
            const hoy = new Date();
            return {
                total: tareas.length,
                pendientes: tareas.filter(t => t.estado === "PENDIENTE").length,
                vencidas: tareas.filter(t => t.estado === "VENCIDA").length,
                hoy: tareas.filter(t => ["PENDIENTE", "VENCIDA"].includes(t.estado) && esMismaFecha(t.vence, hoy)).length,
                completadas: tareas.filter(t => t.estado === "COMPLETADA").length
            };
        }

        function filtrarTareas(tareas) {
            const miId = Number(sesionActual?.id || 0);
            const hoy = new Date();
            if (tareasFiltroActivo === "mias") {
                return tareas.filter(t => miId > 0 && Number(t.asignadoAId) === miId && t.estado !== "COMPLETADA" && t.estado !== "CANCELADA");
            }
            if (tareasFiltroActivo === "hoy") {
                return tareas.filter(t => ["PENDIENTE", "VENCIDA"].includes(t.estado) && esMismaFecha(t.vence, hoy));
            }
            if (tareasFiltroActivo === "vencidas") {
                return tareas.filter(t => t.estado === "VENCIDA");
            }
            if (tareasFiltroActivo === "completadas") {
                return tareas.filter(t => t.estado === "COMPLETADA");
            }
            return tareas.filter(t => t.estado !== "CANCELADA");
        }

        let tareasPagina = 1;
        let ventasPagina = 1;
        async function cargarModuloTareas(vista) {
            const puedeGestionarEquipo = puedeGestionarEquipoCRM();
            const puedeGestionarTareas = tienePermiso("tareas.gestionar");
            if (puedeGestionarEquipo && tareasFiltroActivo === "mias") {
                tareasFiltroActivo = "hoy";
            }
            const [tareasResponse, usuarios] = await Promise.all([
                api(`/api/tareas?pageSize=${obtenerTamanoPagina("tareas")}&page=${tareasPagina}&filtro=${encodeURIComponent(tareasFiltroActivo)}&soloMias=${!puedeGestionarEquipo}`),
                puedeGestionarEquipo ? cargarUsuarios() : Promise.resolve([])
            ]);
            if (!tareasResponse.ok) throw new Error("Tareas no disponibles");
            const pagina = await tareasResponse.json();
            const todasLasTareas = pagina.items || [];
            const miId = Number(sesionActual?.id || 0);
            const tareas = puedeGestionarEquipo
                ? todasLasTareas
                : todasLasTareas.filter(t => miId > 0 && Number(t.asignadoAId) === miId);
            const resumen = pagina.resumen || obtenerResumenTareas(tareas);
            if (resumen.vencidas > 0 && !tareasVencidasNotificadas) {
                tareasVencidasNotificadas = true;
                const mensajeVencidas = `Tienes ${resumen.vencidas} tarea(s) vencida(s).`;
                notificar(mensajeVencidas, "warning");
                // registrarNotificacionSistema() (definida en utils.js) ya
                // tenía todo el soporte visual listo en la campana de
                // notificaciones (icono, estilo "sistema"), pero nada la
                // invocaba: el aviso de tareas vencidas solo vivía en el
                // toast, que desaparece a los pocos segundos.
                if (typeof registrarNotificacionSistema === "function") {
                    registrarNotificacionSistema({
                        id: "tareas-vencidas",
                        titulo: "Tareas vencidas",
                        detalle: mensajeVencidas,
                        tipo: "TAREA"
                    });
                }
            }
            const misPendientes = pagina.resumen?.mias ?? tareas.filter(t =>
                Number(t.asignadoAId) === Number(sesionActual?.id || 0) &&
                t.estado !== "COMPLETADA" &&
                t.estado !== "CANCELADA").length;
            const tareasVisibles = pagina.resumen ? tareas : filtrarTareas(tareas);
            const filtros = [
                ...(puedeGestionarEquipo ? [] : [["mias", "Mis pendientes", misPendientes]]),
                ["hoy", "Hoy", resumen.hoy],
                ["vencidas", "Vencidas", resumen.vencidas],
                ["todas", "Todas", resumen.total],
                ["completadas", "Completadas", resumen.completadas]
            ];

            vista.innerHTML = `
                <div class="module-heading">
                    <div>
                        <h1>Tareas</h1>
                        <p>${puedeGestionarEquipo ? "Seguimientos vencidos y trabajo diario del equipo comercial." : "Tus seguimientos activos: primero vencidas y tareas de hoy."}</p>
                    </div>
                </div>
                <section class="task-summary">
                    <article class="metric-card"><span class="metric-label">Pendientes</span><strong class="metric-value">${resumen.pendientes}</strong></article>
                    <article class="metric-card ${resumen.vencidas ? "danger-card" : ""}"><span class="metric-label">Vencidas</span><strong class="metric-value">${resumen.vencidas}</strong></article>
                    <article class="metric-card"><span class="metric-label">Para hoy</span><strong class="metric-value">${resumen.hoy}</strong></article>
                    <article class="metric-card"><span class="metric-label">Completadas</span><strong class="metric-value">${resumen.completadas}</strong></article>
                </section>
                <section class="task-workspace">
                    ${puedeGestionarTareas ? `<form id="quickTaskForm" class="user-form task-form">
                        <input name="titulo" maxlength="200" placeholder="Nueva tarea o seguimiento" required>
                        <input name="fechaVencimiento" type="datetime-local" required>
                        ${puedeGestionarEquipo ? `<select name="asignadoAId">
                            <option value="">Asignar a...</option>
                            ${usuarios.map(usuario => `<option value="${usuario.id}" ${Number(usuario.id) === Number(sesionActual?.id) ? "selected" : ""}>${escapeHtml(usuario.nombre)}</option>`).join("")}
                        </select>` : ""}
                        <button type="submit">Crear tarea</button>
                    </form>` : ""}
                    <div class="task-filters">
                        ${filtros.map(([id, label, total]) => `<button type="button" class="filter-button ${tareasFiltroActivo === id ? "active" : ""}" data-task-filter="${id}">${label} <span>${total}</span></button>`).join("")}
                    </div>
                    <div class="task-list">
                        ${tareasVisibles.map(tarea => `
                            <article class="task-item ${tarea.estado === "VENCIDA" ? "danger" : ""}">
                                <div>
                                    <strong>${escapeHtml(tarea.titulo)}</strong>
                                    <p>${escapeHtml(tarea.descripcion || "Sin detalle")}</p>
                                    <small>${escapeHtml(tarea.estado)} · ${formatearFecha(tarea.vence)}${tarea.cliente ? ` · ${escapeHtml(tarea.cliente.nombre)}` : ""}${tarea.asignadoA ? ` · ${escapeHtml(tarea.asignadoA)}` : ""}</small>
                                </div>
                                <div class="task-actions">
                                    ${tarea.conversacionId ? `<button type="button" class="mini-action" data-open-task-chat="${tarea.conversacionId}">Abrir chat</button>` : ""}
                                    ${puedeGestionarTareas && tarea.estado !== "COMPLETADA" && tarea.estado !== "CANCELADA" ? `<button type="button" class="mini-action" data-complete-task-module="${tarea.id}">Completar</button>` : ""}
                                </div>
                            </article>`).join("") || '<div class="empty">No hay tareas para este filtro.</div>'}
                    </div>
                    ${renderPaginacion(pagina, "tareas")}
                </section>`;

            enlazarPaginacion(vista, page => { tareasPagina = page; return cargarModuloTareas(vista); });

            vista.querySelector("#quickTaskForm")?.addEventListener("submit", async event => {
                event.preventDefault();
                const datos = Object.fromEntries(new FormData(event.currentTarget));
                datos.asignadoAId = datos.asignadoAId ? Number(datos.asignadoAId) : null;
                if (!puedeGestionarEquipo && sesionActual?.id) {
                    datos.asignadoAId = Number(sesionActual.id);
                }
                const crear = await api("/api/tareas", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(datos)
                });
                if (!crear.ok) {
                    notificar(await crear.text(), "error");
                    return;
                }
                notificar("Tarea creada.", "success");
                await cargarModuloTareas(vista);
            });

            vista.querySelectorAll("[data-task-filter]").forEach(button => {
                button.addEventListener("click", async () => {
                    tareasFiltroActivo = button.dataset.taskFilter;
                    tareasPagina = 1;
                    await cargarModuloTareas(vista);
                });
            });

            vista.querySelectorAll("[data-complete-task-module]").forEach(button => {
                button.addEventListener("click", async () => {
                    const response = await api(`/api/tareas/${button.dataset.completeTaskModule}/completar`, { method: "PUT" });
                    if (!response.ok) {
                        notificar(await response.text(), "error");
                        return;
                    }
                    notificar("Tarea completada.", "success");
                    await cargarModuloTareas(vista);
                });
            });

            vista.querySelectorAll("[data-open-task-chat]").forEach(button => {
                button.addEventListener("click", () => abrirDetalleConversacion(button.dataset.openTaskChat, "tareas"));
            });
        }

        function etapasOportunidad() {
            return ["NUEVA", "CALIFICADA", "PROPUESTA", "NEGOCIACION", "GANADA", "PERDIDA"];
        }

        function resumenVentas(oportunidades) {
            const abiertas = oportunidades.filter(op => !["GANADA", "PERDIDA"].includes(op.etapa));
            const ganadas = oportunidades.filter(op => op.etapa === "GANADA");
            const perdidas = oportunidades.filter(op => op.etapa === "PERDIDA");
            return {
                abiertas: abiertas.length,
                ganadas: ganadas.length,
                perdidas: perdidas.length,
                montoAbierto: abiertas.reduce((total, op) => total + Number(op.monto || 0), 0),
                montoGanado: ganadas.reduce((total, op) => total + Number(op.monto || 0), 0),
                winRate: calcularPorcentaje(ganadas.length, ganadas.length + perdidas.length),
                cierreSemana: abiertas.filter(op => {
                    if (!op.fechaCierreEstimada) return false;
                    const cierre = new Date(op.fechaCierreEstimada);
                    const limite = new Date();
                    limite.setDate(limite.getDate() + 7);
                    return cierre <= limite;
                }).length
            };
        }

        async function cargarModuloVentas(vista) {
            const puedeGestionarEquipo = puedeGestionarEquipoCRM();
            const puedeGestionarVentas = tienePermiso("ventas.gestionar");
            const puedeExportar = tienePermiso("datos.exportar");
            const fechaMinimaCierre = new Date().toISOString().slice(0, 10);
            const [ventasResponse, contactosResponse, usuarios] = await Promise.all([
                api(`/api/oportunidades?pageSize=${obtenerTamanoPagina("ventas")}&page=${ventasPagina}${ventasEtapaFiltro !== "TODAS" ? `&etapa=${encodeURIComponent(ventasEtapaFiltro)}` : ""}`),
                api("/api/crm/contactos"),
                puedeGestionarEquipo ? cargarUsuarios() : Promise.resolve([])
            ]);
            if (!ventasResponse.ok) throw new Error("Ventas no disponibles");
            if (!contactosResponse.ok) throw new Error("Contactos no disponibles");

            const pagina = await ventasResponse.json();
            const oportunidades = pagina.items || [];
            const contactos = await contactosResponse.json();
            const etapas = etapasOportunidad();
            const resumen = pagina.resumen || resumenVentas(oportunidades);
            const oportunidadesVisibles = ventasEtapaFiltro === "TODAS"
                ? oportunidades
                : oportunidades.filter(op => op.etapa === ventasEtapaFiltro);

            vista.innerHTML = `
                <div class="module-heading">
                    <div>
                        <h1>Ventas</h1>
                        <p>Embudo comercial con oportunidades, montos y cierre.</p>
                    </div>
                    ${puedeExportar ? '<button id="salesExportButton" class="secondary-btn" type="button">Exportar CSV</button>' : ""}
                </div>
                <section class="task-summary">
                    <article class="metric-card"><span class="metric-label">Abiertas</span><strong class="metric-value">${resumen.abiertas}</strong></article>
                    <article class="metric-card"><span class="metric-label">Monto abierto</span><strong class="metric-value">${formatearMoneda(resumen.montoAbierto)}</strong></article>
                    <article class="metric-card"><span class="metric-label">Ganadas</span><strong class="metric-value">${resumen.ganadas}</strong></article>
                    <article class="metric-card ${resumen.cierreSemana ? "warning-card" : ""}"><span class="metric-label">Cierre 7 días</span><strong class="metric-value">${resumen.cierreSemana}</strong><small>${resumen.winRate}% win rate</small></article>
                </section>
                <section class="sales-workspace">
                    ${puedeGestionarVentas ? `<form id="quickDealForm" class="user-form sales-form">
                        <input name="titulo" maxlength="200" placeholder="Nueva oportunidad" required>
                        <input id="salesClientSearch" class="sales-client-search" type="search" placeholder="Buscar cliente por nombre o teléfono">
                        <select id="salesClientSelect" name="clienteId" required>
                            <option value="">Cliente...</option>
                            ${contactos.map(contacto => `<option value="${contacto.id}">${escapeHtml(contacto.nombre)} · ${escapeHtml(contacto.telefono || "")}</option>`).join("")}
                        </select>
                        <input name="monto" type="number" min="0.01" step="0.01" placeholder="Monto" required>
                        <select name="moneda"><option value="PEN">PEN</option><option value="USD">USD</option></select>
                        <input name="fechaCierreEstimada" type="date" min="${fechaMinimaCierre}" title="Fecha estimada de cierre" aria-label="Fecha estimada de cierre" required>
                        <input name="probabilidad" type="number" min="0" max="100" step="5" value="10" placeholder="Prob. %">
                        ${puedeGestionarEquipo ? `<select name="usuarioAsignadoId">
                            <option value="">Asesor...</option>
                            ${usuarios.map(usuario => `<option value="${usuario.id}" ${Number(usuario.id) === Number(sesionActual?.id) ? "selected" : ""}>${escapeHtml(usuario.nombre)}</option>`).join("")}
                        </select>` : ""}
                        <button type="submit">Crear venta</button>
                    </form>` : ""}
                    <div class="task-filters">
                        ${["TODAS", ...etapas].map(etapa => {
                            const total = etapa === "TODAS" ? pagina.porEtapa?.reduce((sum, e) => sum + e.cantidad, 0) ?? pagina.total : pagina.porEtapa?.find(e => e.etapa === etapa)?.cantidad ?? oportunidades.filter(op => op.etapa === etapa).length;
                            return `<button type="button" class="filter-button ${ventasEtapaFiltro === etapa ? "active" : ""}" data-sales-filter="${etapa}">${etapa} <span>${total}</span></button>`;
                        }).join("")}
                    </div>
                    <div class="sales-board">
                        ${etapas.map(etapa => {
                            const items = oportunidadesVisibles.filter(op => op.etapa === etapa);
                            const totalEtapa = pagina.porEtapa?.find(e => e.etapa === etapa);
                            if (ventasEtapaFiltro !== "TODAS" && ventasEtapaFiltro !== etapa) return "";
                            return `<section class="sales-column">
                                <div class="stage-header">
                                    <div class="stage-title"><span>${etapa}</span><span>${totalEtapa?.cantidad ?? items.length}</span></div>
                                    <div class="stage-meta">${formatearMoneda(totalEtapa?.monto ?? items.reduce((total, op) => total + Number(op.monto || 0), 0))} · ${items.length} en esta pagina</div>
                                    <div class="stage-bar"></div>
                                </div>
                                ${items.map(op => `
                                    <article class="sales-card">
                                        <div>
                                            <strong>${escapeHtml(op.titulo)}</strong>
                                            <p>${escapeHtml(op.cliente?.nombre || "Sin cliente")} · ${escapeHtml(op.moneda)} ${Number(op.monto || 0).toFixed(2)}</p>
                                            <small>${op.probabilidad}% probabilidad${op.fechaCierreEstimada ? ` · Cierre ${formatearFecha(op.fechaCierreEstimada)}` : " · Sin fecha de cierre"}${op.asesor ? ` · ${escapeHtml(op.asesor)}` : ""}</small>
                                            ${op.motivoPerdida ? `<small>Motivo: ${escapeHtml(op.motivoPerdida)}</small>` : ""}
                                        </div>
                                        ${puedeGestionarVentas ? `<select class="mini-select" data-sales-stage="${op.id}">
                                            ${etapas.map(opcion => `<option value="${opcion}" ${opcion === op.etapa ? "selected" : ""}>${opcion}</option>`).join("")}
                                        </select>` : `<span class="role-badge">${escapeHtml(op.etapa)}</span>`}
                                        <div class="task-actions">
                                            ${op.conversacionId ? `<button type="button" class="mini-action" data-open-sales-chat="${op.conversacionId}">Abrir chat</button>` : ""}
                                        </div>
                                    </article>`).join("") || '<div class="empty">Sin oportunidades en esta pagina.</div>'}
                            </section>`;
                        }).join("")}
                    </div>
                    ${renderPaginacion(pagina, "ventas")}
                </section>`;

            enlazarPaginacion(vista, page => { ventasPagina = page; return cargarModuloVentas(vista); });

            vista.querySelector("#salesExportButton")?.addEventListener("click", () => descargarArchivo(`/api/oportunidades/exportar${ventasEtapaFiltro !== "TODAS" ? `?etapa=${encodeURIComponent(ventasEtapaFiltro)}` : ""}`));

            vista.querySelector("#quickDealForm")?.addEventListener("submit", async event => {
                event.preventDefault();
                const datos = Object.fromEntries(new FormData(event.currentTarget));
                datos.clienteId = Number(datos.clienteId);
                datos.usuarioAsignadoId = datos.usuarioAsignadoId ? Number(datos.usuarioAsignadoId) : null;
                if (!puedeGestionarEquipo && sesionActual?.id) {
                    datos.usuarioAsignadoId = Number(sesionActual.id);
                }
                datos.monto = Number(datos.monto || 0);
                datos.probabilidad = Number(datos.probabilidad || 10);
                datos.fechaCierreEstimada = datos.fechaCierreEstimada || null;
                if (datos.monto <= 0 || !datos.fechaCierreEstimada) {
                    notificar("Completa monto y fecha estimada de cierre para crear la venta.", "warning");
                    return;
                }
                const crear = await api("/api/oportunidades", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(datos)
                });
                if (!crear.ok) {
                    notificar(await crear.text(), "error");
                    return;
                }
                notificar("Oportunidad creada.", "success");
                await cargarModuloVentas(vista);
            });

            let busquedaClientesVersion = 0;
            let busquedaClientesTimer;
            vista.querySelector("#salesClientSearch")?.addEventListener("input", event => {
                const termino = event.currentTarget.value.trim();
                const version = ++busquedaClientesVersion;
                const selector = vista.querySelector("#salesClientSelect");
                clearTimeout(busquedaClientesTimer);
                busquedaClientesTimer = setTimeout(async () => {
                    try {
                        const response = await api(`/api/crm/contactos?page=1&pageSize=50&search=${encodeURIComponent(termino)}`);
                        if (!response.ok) throw new Error("Busqueda no disponible");
                        const data = await response.json();
                        if (version !== busquedaClientesVersion || !selector?.isConnected || moduloActual !== "ventas") return;
                        const selected = selector.selectedOptions[0];
                        const items = Array.isArray(data) ? data : data.items || [];
                        selector.innerHTML = '<option value="">Cliente...</option>' + items.map(c => `<option value="${c.id}">${escapeHtml(c.nombre)} · ${escapeHtml(c.telefono || "")}</option>`).join("");
                        if (selected?.value && !items.some(c => String(c.id) === selected.value)) selector.appendChild(selected);
                        if (selected?.value) selector.value = selected.value;
                    } catch (error) {
                        if (error?.name !== "AbortError") notificar("No se pudo buscar clientes.", "error");
                    }
                }, 250);
            });

            vista.querySelectorAll("[data-sales-filter]").forEach(button => {
                button.addEventListener("click", async () => {
                    ventasEtapaFiltro = button.dataset.salesFilter;
                    ventasPagina = 1;
                    await cargarModuloVentas(vista);
                });
            });

            vista.querySelectorAll("[data-sales-stage]").forEach(select => {
                select.addEventListener("change", async () => {
                    const body = { etapa: select.value };
                    if (select.value === "PERDIDA") {
                        const motivo = await solicitarTextoModal("Motivo de pérdida", "Indica por qué se perdió esta oportunidad");
                        if (!motivo) {
                            await cargarModuloVentas(vista);
                            return;
                        }
                        body.motivoPerdida = motivo;
                    }
                    const response = await api(`/api/oportunidades/${select.dataset.salesStage}/etapa`, {
                        method: "PUT",
                        headers: { "Content-Type": "application/json" },
                        body: JSON.stringify(body)
                    });
                    if (!response.ok) {
                        notificar(await response.text(), "error");
                        return;
                    }
                    notificar("Etapa de venta actualizada.", "success");
                    await cargarModuloVentas(vista);
                });
            });

            vista.querySelectorAll("[data-open-sales-chat]").forEach(button => {
                button.addEventListener("click", () => abrirDetalleConversacion(button.dataset.openSalesChat, "ventas"));
            });
        }
