// Módulo frontend del CRM.

        let leadsPagina = 1;
        async function cargarModuloLeads(vista) {
            // El endpoint ahora pagina (antes traía todo sin límite).
            // Pedimos un lote grande para mantener el kanban tal como
            // estaba; una vista con "cargar más" es un paso pendiente.
            const response = await api(`/api/crm/leads?pageSize=${obtenerTamanoPagina("leads")}&page=${leadsPagina}`);
            if (!response.ok) throw new Error("Leads no disponibles");
            const paginaLeads = await response.json();
            const conversacionesLeads = paginaLeads.items || [];
            const puedeAsignar = tienePermiso("conversaciones.asignar");
            const puedeCambiarEstado = tienePermiso("conversaciones.atender");
            const etapas = estadosConversacionPorRol().map(etapa => etapa.id);
            let usuarios = [];
            if (puedeAsignar) {
                const usuariosResponse = await api("/api/crm/usuarios");
                if (!usuariosResponse.ok) throw new Error("Usuarios no disponibles");
                usuarios = await usuariosResponse.json();
            }

            const iniciales = nombre => (nombre || "C")
                .split(" ")
                .slice(0, 2)
                .map(parte => parte.charAt(0))
                .join("")
                .toUpperCase();
            const renderAsesor = item => {
                const asesorNombre = item.asesor || "Sin asignar";
                const avatar = item.asesor ? iniciales(asesorNombre) : "?";
                return `<div class="assignee-profile">
                    <button type="button" class="assignee-avatar" title="${escapeAttribute(asesorNombre)}" aria-label="Ver asesor asignado">
                        ${escapeHtml(avatar)}
                    </button>
                    <div class="assignee-popover hidden">
                        <div class="assignee-popover-head">
                            <span class="assignee-avatar large">${escapeHtml(avatar)}</span>
                            <div>
                                <strong>${escapeHtml(asesorNombre)}</strong>
                                <span>${item.asesor ? "Asesor asignado" : "Pendiente de asignación"}</span>
                            </div>
                        </div>
                        ${puedeAsignar ? `<select class="assignee-select" data-id="${item.id}">
                            <option value="">Sin asignar</option>
                            ${usuarios.map(usuario => `<option value="${usuario.id}" ${usuario.nombre === item.asesor ? "selected" : ""}>${escapeHtml(usuario.nombre)} · ${escapeHtml(usuario.rol)}</option>`).join("")}
                        </select>` : ""}
                    </div>
                </div>`;
            };

            vista.innerHTML = `
                        <div class="module-heading">
                            <div><h1>Leads</h1><p>Organiza los chats privados por etapa. Los comentarios públicos se gestionan en Marketing.</p></div>
                            ${puedeAsignar
                                ? '<button type="button" id="btnAsignarPendientes" class="secondary-btn">Asignar pendientes automáticamente</button>'
                                : ""}
                        </div>
                        <div class="kanban-summary">
                            ${etapas.map(etapa => {
                                const total = paginaLeads.porEstado?.find(item => item.estado === etapa)?.cantidad ?? conversacionesLeads.filter(item => item.estado === etapa).length;
                                return `<div class="kanban-summary-card" data-stage="${etapa}">
                                    <span>${etapa}</span>
                                    <strong>${total}</strong>
                                </div>`;
                            }).join("")}
                        </div>
                        <div class="stage-columns">${etapas.map(etapa => {
                            const items = conversacionesLeads.filter(item => item.estado === etapa);
                            const totalEtapa = paginaLeads.porEstado?.find(e => e.estado === etapa)?.cantidad ?? items.length;
                            return `
                            <div class="stage-column" data-stage="${etapa}">
                                <div class="stage-header">
                                    <div class="stage-title"><span>${etapa}</span><span>${totalEtapa}</span></div>
                                    <div class="stage-meta">${items.length} de ${totalEtapa} en esta pagina</div>
                                    <div class="stage-bar"></div>
                                </div>
                                ${items.map(item => {
                                    const canal = String(item.canal || "WHATSAPP").toUpperCase();
                                    const red = typeof obtenerRedPorCanal === "function"
                                        ? obtenerRedPorCanal(canal)
                                        : { nombre: canal, clase: canal.toLowerCase() };
                                    return `<div class="deal-card" data-conversation-id="${item.id}" draggable="${puedeCambiarEstado}">
                                <div class="deal-card-top">
                                    <div class="deal-avatar">${escapeHtml(iniciales(item.cliente.nombre))}</div>
                                    <div class="deal-info">
                                        <strong>${escapeHtml(item.cliente.nombre)}</strong>
                                        <span class="deal-id">Conversación #${item.id}</span>
                                    </div>
                                    ${renderAsesor(item)}
                                </div>
                                <div class="deal-meta-row">
                                    <span>${crearLogoRed(red.clase || "whatsapp")} ${escapeHtml(red.nombre || "WhatsApp")} · ${escapeHtml(item.cliente.telefono)}</span>
                                    <span class="deal-stage-pill">${escapeHtml(item.estado)}</span>
                                </div>
                                <div class="deal-meta-row">
                                    ${item.proximaTarea
                                        ? `<span class="${item.proximaTarea.vencida ? "danger-text" : ""}">Próximo paso: ${escapeHtml(item.proximaTarea.titulo)} · ${formatearFecha(item.proximaTarea.vence)}</span>`
                                        : '<span class="danger-text">Sin próximo paso</span>'}
                                </div>
                                ${puedeCambiarEstado ? `<select class="stage-select" data-id="${item.id}">
                                    ${etapas.map(opcion => `<option value="${opcion}" ${opcion === item.estado ? "selected" : ""}>${opcion}</option>`).join("")}
                                </select>` : ""}
                            </div>`;
                                }).join("") || '<div class="empty">Sin conversaciones</div>'}
                            </div>`;
                        }).join("")}</div>${renderPaginacion(paginaLeads, "leads")}`;

            enlazarPaginacion(vista, page => { leadsPagina = page; return cargarModuloLeads(vista); });

            vista.querySelectorAll(".stage-select").forEach(select => {
                select.addEventListener("change", () => cambiarEstado(select.dataset.id, select.value));
            });
            vista.querySelectorAll(".assignee-select").forEach(select => {
                select.addEventListener("change", () => asignarConversacion(select.dataset.id, select.value));
            });
            vista.querySelectorAll(".assignee-avatar").forEach(button => {
                button.addEventListener("click", event => {
                    event.stopPropagation();
                    const popover = button.closest(".assignee-profile")?.querySelector(".assignee-popover");
                    vista.querySelectorAll(".assignee-popover").forEach(item => {
                        if (item !== popover) item.classList.add("hidden");
                    });
                    popover?.classList.toggle("hidden");
                });
            });
            vista.addEventListener("click", event => {
                if (!event.target.closest(".assignee-profile")) {
                    vista.querySelectorAll(".assignee-popover").forEach(item => item.classList.add("hidden"));
                }
            });

            /*
             * Al hacer click en la tarjeta (fuera de los <select>) abrimos
             * SOLO esa conversación, igual que en Kommo al hacer click sobre
             * un lead: no lleva al inbox general con todos los
             * chats, sino a una vista de detalle con la flecha "<" para volver
             * aquí mismo.
             */
            vista.querySelectorAll(".deal-card").forEach(card => {
                card.addEventListener("dragstart", event => {
                    if (!puedeCambiarEstado) {
                        event.preventDefault();
                        return;
                    }
                    event.dataTransfer.setData("text/plain", card.dataset.conversationId || "");
                    event.dataTransfer.effectAllowed = "move";
                    card.classList.add("dragging");
                });
                card.addEventListener("dragend", () => {
                    card.classList.remove("dragging");
                });
                card.addEventListener("click", event => {
                    if (event.target.closest("select") || event.target.closest(".assignee-profile")) return;
                    abrirDetalleConversacion(card.dataset.conversationId, "leads");
                });
            });
            vista.querySelectorAll(".stage-column").forEach(column => {
                column.addEventListener("dragover", event => {
                    if (!puedeCambiarEstado) return;
                    event.preventDefault();
                    event.dataTransfer.dropEffect = "move";
                    column.classList.add("drag-over");
                });
                column.addEventListener("dragleave", () => {
                    column.classList.remove("drag-over");
                });
                column.addEventListener("drop", async event => {
                    if (!puedeCambiarEstado) return;
                    event.preventDefault();
                    column.classList.remove("drag-over");
                    const id = event.dataTransfer.getData("text/plain");
                    const estado = column.dataset.stage;
                    if (!id || !estado) return;
                    await cambiarEstado(id, estado);
                });
            });

            const btnAsignarPendientes = document.getElementById("btnAsignarPendientes");
            if (btnAsignarPendientes) {
                btnAsignarPendientes.addEventListener("click", async () => {
                    btnAsignarPendientes.disabled = true;
                    btnAsignarPendientes.textContent = "Asignando...";
                    try {
                        const response = await api("/api/crm/conversaciones/asignar-pendientes", { method: "POST" });
                        if (!response.ok) throw new Error("No se pudo asignar");
                        const resultado = await response.json();
                        await cargarModuloLeads(vista);
                        notificar(resultado.asignadas > 0
                            ? `Se asignaron ${resultado.asignadas} conversación(es) sin asesor.`
                            : "No había conversaciones pendientes por asignar.", "success");
                    } catch (error) {
                        console.error(error);
                        notificar("No se pudieron asignar las conversaciones pendientes.", "error");
                        btnAsignarPendientes.disabled = false;
                        btnAsignarPendientes.textContent = "Asignar pendientes automáticamente";
                    }
                });
            }
        }

        /*
         * Abre UNA sola conversación en modo "detalle", igual que Kommo al
         * entrar a un lead: se ve el hilo de mensajes y la ficha del cliente,
         * pero no la bandeja completa con la lista de todos los chats.
         * moduloOrigen indica a qué pantalla debe volver la flecha "<".
         */
        async function abrirDetalleConversacion(id, moduloOrigen = "leads") {
            modoDetalleConversacion = true;
            moduloRetornoDetalle = moduloOrigen;

            document.querySelectorAll(".nav-item").forEach(item => {
                item.classList.toggle("active", item.dataset.module === moduloOrigen);
            });

            document.getElementById("moduleView").classList.add("hidden");
            document.querySelector(".sidebar").classList.add("hidden");
            document.querySelector(".chat").classList.remove("hidden");
            document.getElementById("details").classList.remove("hidden");
            document.getElementById("leadDetailBar").classList.remove("hidden");
            if (window.lucide) window.lucide.createIcons();

            await seleccionarConversacion(id);
        }

        async function asignarConversacion(id, usuarioId) {
            const response = await api(`/api/crm/conversaciones/${id}/asignar`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ usuarioId: usuarioId ? Number(usuarioId) : null })
            });
            if (!response.ok) throw new Error("No se pudo asignar la conversación");
            await cargarModuloLeads(document.getElementById("moduleView"));
            notificar("Conversación reasignada.");
        }

        async function tomarConversacion(id) {
            const response = await api(`/api/crm/conversaciones/${id}/tomar`, {
                method: "PUT"
            });
            if (!response.ok) {
                notificar(await obtenerMensajeError(response, "No se pudo tomar la conversación."), "error");
                return;
            }
            await cargarConversaciones();
            await seleccionarConversacion(id);
            notificar("Conversación tomada. El bot quedó pausado para este chat.", "success");
        }

        async function cambiarEstado(id, estadoNuevo) {
            const response = await api(`/api/crm/conversaciones/${id}/estado`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ estado: estadoNuevo })
            });
            if (!response.ok) throw new Error("No se pudo cambiar el estado");
            await cargarModuloLeads(document.getElementById("moduleView"));
            notificar("Estado actualizado.");
        }
