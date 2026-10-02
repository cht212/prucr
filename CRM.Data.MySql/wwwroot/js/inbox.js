// Módulo frontend del CRM.

        function actualizarLabelFiltroAsesor(valorSeleccionado) {
            const valor = valorSeleccionado || "";
            const toggle = document.getElementById("inboxAdvisorToggle");
            const label = document.getElementById("inboxAdvisorLabel");
            const avatar = document.getElementById("inboxAdvisorAvatar");
            const menu = document.getElementById("inboxAdvisorMenu");

            if (!toggle || !label || !avatar || !menu) return;

            const itemActual = [...menu.querySelectorAll(".inbox-advisor-item")]
                .find(item => item.dataset.userId === valor) ||
                [...menu.querySelectorAll(".inbox-advisor-item")]
                    .find(item => item.dataset.userId === "");

            const texto = itemActual?.dataset.userLabel || "Sin filtro";
            const inicial = texto.trim().charAt(0)?.toUpperCase() || "A";

            label.textContent = texto;
            avatar.textContent = inicial;
            menu.querySelectorAll(".inbox-advisor-item").forEach(item => {
                const activo = item.dataset.userId === valor;
                item.classList.toggle("active", activo);
            });
            toggle.setAttribute("aria-expanded", "false");
            menu.classList.add("hidden");
        }

        function alternarMenuAsesor() {
            const toggle = document.getElementById("inboxAdvisorToggle");
            const menu = document.getElementById("inboxAdvisorMenu");
            if (!toggle || !menu) return;

            const abierto = menu.classList.contains("hidden");
            menu.classList.toggle("hidden", !abierto);
            toggle.setAttribute("aria-expanded", String(abierto));
        }

        async function cargarUsuariosInbox() {
            try {
                const esAsesor = !tienePermiso("comunicaciones.chats.todos");
                // El asesor no necesita consultar el directorio completo: la
                // API ya limita la bandeja a sus conversaciones y a las que
                // están disponibles para tomar.
                const usuarios = esAsesor
                    ? [{
                        id: Number(sesionActual?.id || 0),
                        usuario: sesionActual?.usuario || "Asesor",
                        nombre: sesionActual?.usuario || "Asesor",
                        rol: sesionActual?.rol || "Asesor"
                    }]
                    : await cargarUsuarios();
                usuariosCache = usuarios;
                const menu = document.getElementById("inboxAdvisorMenu");
                if (esAsesor && sesionActual?.id) {
                    asesorFiltroActivo = String(sesionActual.id);
                }
                const valorActual = asesorFiltroActivo || "";
                if (!menu) return;

                const opciones = esAsesor
                    ? []
                    : [
                        { id: "", label: "Sin filtro", avatar: "A", neutral: false },
                        { id: "unassigned", label: "Sin asesor", avatar: "U", neutral: true }
                    ];

                usuarios
                    .filter(usuario => !esAsesor || Number(usuario.id) === Number(sesionActual?.id || 0))
                    .forEach(usuario => {
                        opciones.push({
                            id: String(usuario.id),
                            label: usuario.nombre || usuario.usuario || `Usuario ${usuario.id}`,
                            avatar: (usuario.nombre || usuario.usuario || `Usuario ${usuario.id}`).trim().charAt(0).toUpperCase() || "U",
                            neutral: false
                        });
                    });

                menu.innerHTML = opciones.map(opcion => `
                    <button class="inbox-advisor-item ${opcion.id === valorActual ? "active" : ""}" type="button" data-user-id="${opcion.id}" data-user-label="${escapeHtml(opcion.label)}">
                        <span class="inbox-advisor-avatar mini ${opcion.neutral ? "neutral" : ""}">${escapeHtml(opcion.avatar)}</span>
                        <span>${escapeHtml(opcion.label)}</span>
                    </button>
                `).join("");

                menu.querySelectorAll(".inbox-advisor-item").forEach(item => {
                    item.addEventListener("click", () => {
                        const nuevoValor = item.dataset.userId || "";
                        if (esAsesor) {
                            asesorFiltroActivo = String(sesionActual?.id || "");
                        } else {
                            asesorFiltroActivo = nuevoValor === "unassigned" ? "unassigned" : nuevoValor;
                        }
                        actualizarLabelFiltroAsesor(asesorFiltroActivo);
                        recargarBandeja();
                        if (typeof cargarModuloDashboard === "function" && moduloActual === "dashboard") {
                            const vista = document.querySelector("#moduleView .module-content") || document.getElementById("moduleView");
                            if (vista) cargarModuloDashboard(vista);
                        }
                        if (moduloActual === "reportes") {
                            const vista = document.querySelector("#moduleView .module-content") || document.getElementById("moduleView");
                            if (vista && typeof cargarModuloReportes === "function") {
                                // Antes reportesFiltros.usuarioId nunca se actualizaba
                                // aquí: al cambiar de asesor en el inbox, el módulo de
                                // Reportes se recargaba pero seguía mostrando el filtro
                                // anterior. sincronizarFiltroReportesDesdeAsesor() existía
                                // en el archivo pero no la llamaba nadie.
                                if (typeof sincronizarFiltroReportesDesdeAsesor === "function") {
                                    sincronizarFiltroReportesDesdeAsesor();
                                }
                                cargarModuloReportes(vista);
                            }
                        }
                    });
                });

                actualizarLabelFiltroAsesor(valorActual);
            } catch (error) {
                console.error("No se pudieron cargar los usuarios para el filtro del inbox", error);
            }
        }

        document.addEventListener("click", event => {
            const toggle = document.getElementById("inboxAdvisorToggle");
            const menu = document.getElementById("inboxAdvisorMenu");

            if (!toggle || !menu) return;
            const clicFuera = !toggle.contains(event.target) && !menu.contains(event.target);
            if (clicFuera) {
                menu.classList.add("hidden");
                toggle.setAttribute("aria-expanded", "false");
            }
        });

        document.getElementById("inboxAdvisorToggle")?.addEventListener("click", event => {
            event.preventDefault();
            event.stopPropagation();
            alternarMenuAsesor();
        });

        let cargaBandejaVersion = 0;
        async function cargarConversaciones() {
            const version = ++cargaBandejaVersion;
            const params = new URLSearchParams({ page: String(inboxPagina), pageSize: String(obtenerTamanoPagina("inbox")), canal: inboxCanalActivo, filtro: filtroActivo });
            const search = document.getElementById("searchInput").value.trim();
            if (search) params.set("search", search);
            if (asesorFiltroActivo && (!esRol("asesor") || tienePermiso("comunicaciones.chats.todos"))) params.set("asesor", asesorFiltroActivo);
            const response = await api(`/api/whatsapp/conversaciones?${params}`);
            if (!response.ok) throw new Error("No se pudieron cargar las conversaciones");
            const pagina = await response.json();
            if (version !== cargaBandejaVersion) return;
            const nuevasConversaciones = Array.isArray(pagina) ? pagina : pagina.items || [];
            inboxPaginacion = Array.isArray(pagina) ? { total: pagina.length, page: 1, pageSize: 50 } : pagina;
            procesarNotificacionesMensajesCliente(nuevasConversaciones);
            conversaciones = nuevasConversaciones;
            document.getElementById("conversationCount").textContent =
                `${inboxPaginacion.total} ${Number(inboxPaginacion.total) === 1 ? "chat" : "chats"}`;
            actualizarNotificacionesComunicaciones();
            await cargarUsuariosInbox();
            mostrarConversaciones();
        }

        function recargarBandeja() {
            inboxPagina = 1;
            return cargarConversaciones().catch(error => {
                if (error?.name !== "AbortError") notificar("No se pudo actualizar la bandeja.", "error");
            });
        }

        function mostrarPaginacionBandeja() {
            lista.insertAdjacentHTML("beforeend", renderPaginacion(inboxPaginacion, "inbox"));
            enlazarPaginacion(lista, page => {
                inboxPagina = page;
                return cargarConversaciones().catch(error => {
                    if (error?.name !== "AbortError") notificar("No se pudo cargar la pagina.", "error");
                });
            });
        }

        function obtenerTiempo(fecha) {
            if (!fecha) return 0;
            const tiempo = new Date(fecha).getTime();
            return Number.isNaN(tiempo) ? 0 : tiempo;
        }

        function obtenerVistaPreviaUltimoMensaje(conversacion) {
            const tipo = (conversacion.ultimoMensajeTipo || "text").toLowerCase();
            const direccion = (conversacion.ultimoMensajeDireccion || "").toUpperCase();
            const prefijo = direccion === "S"
                ? (tipo === "bot" ? "Bot: " : "Tú: ")
                : "";
            const texto = String(conversacion.ultimoMensajeTexto || "").trim();

            if (tipo === "comment") {
                return `${prefijo}💬 Comentario: ${texto.replace(/^Comentario:\s*/i, "")}`;
            }
            if (tipo === "image") return `${prefijo}📷 Foto`;
            if (tipo === "audio" || tipo === "voice") return `${prefijo}🎤 Audio`;
            if (tipo === "video") return `${prefijo}🎥 Video`;
            if (tipo === "sticker") return `${prefijo}Sticker`;
            if (tipo === "location") return `${prefijo}📍 Ubicación`;
            if (tipo === "contacts" || tipo === "contact") return `${prefijo}👤 Contacto`;

            if (tipo === "document" || tipo === "application/pdf") {
                let nombre = "Documento";
                try {
                    const archivo = JSON.parse(texto);
                    nombre = archivo?.nombre || nombre;
                } catch {
                    // Los adjuntos antiguos pueden contener solamente la URL.
                }
                return `${prefijo}📎 ${nombre}`;
            }

            return texto ? `${prefijo}${texto}` : "Sin mensajes todavía";
        }

        function procesarNotificacionesMensajesCliente(nuevasConversaciones) {
            const miId = Number(sesionActual?.id || 0);
            nuevasConversaciones.forEach(conversacion => {
                const tiempoActual = obtenerTiempo(conversacion.ultimoMensajeCliente);
                const tiempoAnterior = ultimosMensajesCliente.get(conversacion.id) || 0;
                const asignadoAMi = !conversacion.usuarioAsignadoId || miId <= 0 || Number(conversacion.usuarioAsignadoId) === miId;

                if (
                    notificacionesConversacionesInicializadas &&
                    asignadoAMi &&
                    conversacion.requiereAtencion &&
                    tiempoActual > tiempoAnterior
                ) {
                    mostrarNotificacionMensajeCliente(conversacion, true);
                }

                ultimosMensajesCliente.set(conversacion.id, tiempoActual);
            });
            notificacionesConversacionesInicializadas = true;
        }

        function mostrarNotificacionMensajeCliente(conversacion, mostrarToast = true) {
            const nombre = conversacion.nombre || "Cliente";
            const telefono = conversacion.telefono || "";
            const red = obtenerRedPorCanal(obtenerCanalConversacion(conversacion));
            const abrir = () => abrirConversacionDesdeNotificacion(conversacion.id);
            registrarNotificacionMensaje(conversacion);
            if (!mostrarToast) return;

            notificar(`
                <span class="toast-avatar">${escapeHtml(obtenerIniciales(nombre))}</span>
                <span class="toast-content">
                    <strong>Nuevo mensaje</strong>
                    <span>${escapeHtml(nombre)}</span>
                    <small>${escapeHtml(red.nombre)} · ${escapeHtml(telefono || "Click para abrir la conversación")}</small>
                </span>
            `, "message rich", abrir, true);
        }

        async function abrirConversacionDesdeNotificacion(id) {
            const sidebarVisible = !document.querySelector(".sidebar")?.classList.contains("hidden");
            if (sidebarVisible) {
                await seleccionarConversacion(id);
                return;
            }

            await abrirDetalleConversacion(id, moduloActual || "dashboard");
        }

        // normalizarCanal(), obtenerCanalConversacion() y obtenerRedPorCanal()
        // viven ahora en utils.js (catálogo REDES_DISPONIBLES) para no tener
        // dos implementaciones del mismo nombre compitiendo entre archivos.

        function renderFiltrosRedBandeja() {
            const contenedor = document.getElementById("inboxNetworkFilters");
            if (!contenedor) return;
            const red = obtenerRedPorCanal(inboxCanalActivo === "TODOS" ? "TODOS" : inboxCanalActivo);
            contenedor.innerHTML = `
                <div class="inbox-channel-context" role="img" title="${escapeAttribute(red.nombre)}" aria-label="Red activa: ${escapeAttribute(red.nombre)}">
                    ${crearLogoRed(red.clase)}
                </div>`;
        }

        function sincronizarSubmenuComunicaciones() {
            const grupo = document.querySelector('[data-nav-group="comunicaciones"]');
            grupo?.classList.toggle("expanded", comunicacionesMenuAbierto);
            const toggle = grupo?.querySelector(".nav-group-toggle");
            toggle?.setAttribute("aria-expanded", String(comunicacionesMenuAbierto));
            if (toggle) toggle.title = comunicacionesMenuAbierto ? "Ocultar canales" : "Mostrar canales de comunicación";
            document.querySelectorAll(".nav-subitem[data-channel]").forEach(item => {
                item.classList.toggle("active", normalizarCanal(item.dataset.channel) === inboxCanalActivo);
            });
        }

        function cambiarCanalBandeja(canal, opciones = {}) {
            const nuevoCanal = normalizarCanal(canal || "TODOS");
            if (nuevoCanal !== "TODOS" && !puedeVerCanalComunicaciones(nuevoCanal)) return;
            const cambioCanal = inboxCanalActivo !== nuevoCanal;
            inboxCanalActivo = nuevoCanal;
            comunicacionesMenuAbierto = true;
            renderFiltrosRedBandeja();
            sincronizarSubmenuComunicaciones();
            if (
                cambioCanal &&
                nuevoCanal !== "TODOS" &&
                conversacionSeleccionada &&
                obtenerCanalConversacion(conversacionSeleccionada) !== nuevoCanal
            ) {
                limpiarConversacionSeleccionada();
            }
            inboxPagina = 1;
            mostrarConversaciones();

            if (opciones.abrirModulo !== false && (moduloActual !== "inbox" || cambioCanal)) {
                abrirModulo("inbox");
            }
        }

        function limpiarConversacionSeleccionada() {
            ++seleccionConversacionVersion;
            conversacionSeleccionada = null;
            limpiarRespuestaSeleccionada();
            document.getElementById("chatHeader").classList.remove("chat-header--conversation");
            ultimoAvisoEscribiendo = 0;
            document.getElementById("chatHeader").innerHTML = `
                <div class="chat-name">Selecciona una conversacion</div>
                <div class="chat-phone">-</div>`;
            mensajes.innerHTML = '<div class="empty">Selecciona una conversacion para ver los mensajes.</div>';
            quickReplies?.classList.add("hidden");
            if (quickReplies) quickReplies.innerHTML = "";
            document.getElementById("details").innerHTML =
                '<div class="details-title">Ficha del cliente</div><div class="empty-detail">Selecciona una conversacion para ver sus datos y actividad.</div>';
            input.value = "";
            input.disabled = true;
            boton.disabled = true;
            attachButton.disabled = true;
        }

        function actualizarNotificacionesComunicaciones() {
            const badge = document.getElementById("communicationsBadge");
            if (!badge) return;
            const pendientes = inboxPaginacion.pendientes ?? conversaciones.filter(c => {
                const asignadoAMi = !c.usuarioAsignadoId || !sesionActual?.id || Number(c.usuarioAsignadoId) === Number(sesionActual.id);
                return c.requiereAtencion && asignadoAMi;
            }).length;
            badge.textContent = pendientes > 99 ? "99+" : String(pendientes);
            badge.classList.toggle("hidden", pendientes === 0);
        }

        // Refleja en el filtro de Reportes el asesor elegido en el selector
        // de la bandeja. Antes exigía esRol("asesor") para actuar, pero ese
        // selector está oculto para el rol asesor y "reportes" ni siquiera
        // está en su menú (ver MODULOS_POR_ROL en api-session.js), así que
        // la condición nunca se cumplía: quien realmente cambia de asesor
        // desde el inbox mientras ve Reportes es un administrador o
        // supervisor.
        function sincronizarFiltroReportesDesdeAsesor() {
            if (!asesorFiltroActivo || asesorFiltroActivo === "unassigned") {
                reportesFiltros.usuarioId = "";
                return;
            }
            reportesFiltros.usuarioId = String(asesorFiltroActivo);
        }

        function mostrarConversaciones() {
            const filtro = document.getElementById("searchInput").value.toLowerCase().trim();
            const resultado = conversaciones.filter(c => {
                const canal = obtenerCanalConversacion(c);
                const coincideCanal = inboxCanalActivo === "TODOS" || canal === inboxCanalActivo;
                const coincideEstado = filtroActivo === "all" ||
                    (filtroActivo === "new" && (c.estado || "").toUpperCase() === "NUEVO") ||
                    (filtroActivo === "open" && ["ABIERTO", "EN_ATENCION"].includes((c.estado || "").toUpperCase())) ||
                    (filtroActivo === "mine" && sesionActual?.id && Number(c.usuarioAsignadoId) === Number(sesionActual.id)) ||
                    (filtroActivo === "unassigned" && !c.usuarioAsignadoId) ||
                    (filtroActivo === "pending" && c.requiereAtencion);
                // Para un asesor, la autorización y el alcance ya fueron
                // aplicados en el servidor. Repetir el filtro aquí hacía que
                // el contador tuviera chats pero la lista apareciera vacía.
                const coincideAsesor = !tienePermiso("comunicaciones.chats.todos")
                    ? true
                    : asesorFiltroActivo === "" ||
                        (asesorFiltroActivo === "unassigned" && !c.usuarioAsignadoId) ||
                        (asesorFiltroActivo && Number(c.usuarioAsignadoId) === Number(asesorFiltroActivo));
                const coincideBusqueda =
                    (c.nombre || "").toLowerCase().includes(filtro) ||
                    (c.telefono || "").includes(filtro);
                return coincideCanal && coincideEstado && coincideAsesor && coincideBusqueda;
            }).sort((a, b) => {
                const atencionA = a.requiereAtencion ? 1 : 0;
                const atencionB = b.requiereAtencion ? 1 : 0;
                if (atencionA !== atencionB) return atencionB - atencionA;

                if (a.requiereAtencion && b.requiereAtencion) {
                    return obtenerTiempo(a.ultimoMensajeCliente || a.ultimoMensaje) -
                        obtenerTiempo(b.ultimoMensajeCliente || b.ultimoMensaje);
                }

                return obtenerTiempo(b.ultimoMensaje) - obtenerTiempo(a.ultimoMensaje);
            });

            lista.innerHTML = "";
            if (!resultado.length) {
                lista.innerHTML = '<div class="empty">No hay conversaciones para este filtro.</div>';
                mostrarPaginacionBandeja();
                return;
            }

            resultado.forEach(conversacion => {
                const red = obtenerRedPorCanal(obtenerCanalConversacion(conversacion));
                const vistaPrevia = obtenerVistaPreviaUltimoMensaje(conversacion);
                const esComentario = String(conversacion.ultimoMensajeTipo || "").toLowerCase() === "comment";
                const textoAtencion = conversacion.requiereAsignacion || !conversacion.usuarioAsignadoId
                    ? "Sin asesor"
                    : "Respuesta pendiente";
                const textoAsignado = conversacion.usuarioAsignado
                    ? `Asesor: ${conversacion.usuarioAsignado}`
                    : "";
                const elemento = document.createElement("div");
                elemento.className = "conversation" +
                    (conversacionSeleccionada && conversacionSeleccionada.id === conversacion.id ? " active" : "") +
                    (conversacion.requiereAtencion ? " needs-attention" : "") +
                    (esComentario ? " has-public-comment" : "");
                elemento.innerHTML = `
                            <div class="conversation-row">
                            <span class="conversation-avatar">${escapeHtml(obtenerIniciales(conversacion.nombre || conversacion.telefono || "C"))}</span>
                            <div class="conversation-body">
                            <div class="conversation-top">
                                <div class="conversation-name">${escapeHtml(conversacion.nombre || "Sin nombre")}</div>
                                <div class="conversation-time">${formatearFecha(conversacion.ultimoMensaje)}</div>
                            </div>
                            <div class="conversation-channel">
                                ${crearLogoRed(red.clase)}
                                <span>${escapeHtml(red.nombre)}</span>
                                ${esComentario ? '<span class="conversation-origin-badge">Comentario público</span>' : ""}
                            </div>
                            <div class="conversation-preview" title="${escapeAttribute(vistaPrevia)}">${escapeHtml(vistaPrevia)}</div>
                            ${textoAsignado ? `<div class="conversation-advisor">${escapeHtml(textoAsignado)}</div>` : ""}
                            ${conversacion.requiereAtencion ? `<div class="conversation-alert">${textoAtencion}</div>` : ""}
                            <div class="conversation-status">${escapeHtml(conversacion.estado || "")}</div>
                            </div></div>`;
                elemento.addEventListener("click", () => seleccionarConversacion(conversacion.id));
                lista.appendChild(elemento);
            });
            mostrarPaginacionBandeja();
        }

        let seleccionConversacionVersion = 0;

        async function seleccionarConversacion(id, opciones = {}) {
            const refresco = opciones.refresco === true;
            if (refresco && Number(conversacionSeleccionada?.id) !== Number(id)) return;
            const version = refresco ? seleccionConversacionVersion : ++seleccionConversacionVersion;
            const navigationSignal = window.__crmNavigationController?.signal;
            const refrescarFicha = opciones.refrescarFicha !== false;
            if (!refresco) document.body.classList.remove("mobile-contact-details-open");
            if (!refresco && Number(conversacionSeleccionada?.id || 0) !== Number(id)) {
                limpiarRespuestaSeleccionada();
                input.value = "";
            }
            const response = await api(`/api/whatsapp/conversaciones/${id}`);
            if (!response.ok) throw new Error("No se pudo cargar la conversacion");
            const data = await response.json();
            if (version !== seleccionConversacionVersion || navigationSignal?.aborted ||
                (refresco && Number(conversacionSeleccionada?.id) !== Number(id))) return;
            if (refresco) {
                const existentes = conversacionSeleccionada.mensajes || [];
                const nuevos = data.mensajes || [];
                const idsNuevos = new Set(nuevos.map(m => String(m.id)));
                // Si llegan mas mensajes que un bloque completo, conserva un cursor
                // continuo para poder recuperar los que quedaron entre ambos bloques.
                if (!existentes.length || existentes.some(m => idsNuevos.has(String(m.id)))) {
                    data.mensajes = [...existentes.filter(m => !idsNuevos.has(String(m.id)) && Number(m.id) > 0), ...nuevos]
                        .sort((a, b) => new Date(a.fecha) - new Date(b.fecha) || Number(a.id) - Number(b.id));
                    data.hayMasMensajes = conversacionSeleccionada.hayMasMensajes;
                    data.mensajeMasAntiguoId = conversacionSeleccionada.mensajeMasAntiguoId;
                }
                if (JSON.stringify(data) === JSON.stringify(conversacionSeleccionada)) return;
            }
            conversacionSeleccionada = data;
            if (!refresco) document.body.classList.add("mobile-chat-open");
            ultimoAvisoEscribiendo = 0;
            mostrarConversaciones();
            mostrarConversacion({ refrescarFicha, conservarScroll: refresco });
        }

        function mostrarConversacion(opciones = {}) {
            const refrescarFicha = opciones.refrescarFicha !== false;
            const conversacion = conversacionSeleccionada;
            const cliente = conversacion.cliente;
            const red = obtenerRedPorCanal(obtenerCanalConversacion(conversacion));
            const botActivo = (conversacion.bot?.estado || "ACTIVO").toUpperCase() === "ACTIVO";
            const asesor = conversacion.usuarioAsignado;
            const asesorId = Number(asesor?.id || 0);
            const puedeSolicitarReasignacion = esRol("asesor") &&
                asesorId === Number(sesionActual?.id || 0);
            const puedeControlarBot = tienePermiso("conversaciones.atender") &&
                conversacion.puedeAtender !== false &&
                (!esRol("asesor") || asesorId === Number(sesionActual?.id || 0));
            const nombreAsesor = typeof asesor === "string" ? asesor : asesor?.nombre || asesor?.usuario || "Sin asesor";
            const contieneComentarios = (conversacion.mensajes || []).some(mensaje =>
                String(mensaje.tipo || "").toLowerCase() === "comment");
            const detalleContacto = [cliente.telefono, nombreAsesor].filter(Boolean).join(" · ");
            const puedeVerFicha = tienePermiso("comunicaciones.ficha") || tienePermiso("comunicaciones.ficha.contacto");
            document.getElementById("chatHeader").classList.add("chat-header--conversation");
            document.getElementById("chatHeader").innerHTML = `
                        <button id="mobileChatBack" class="mobile-chat-back" type="button" aria-label="Volver a conversaciones"><i data-lucide="arrow-left"></i></button>
                        <div class="chat-title-row">
                            <div class="chat-contact-summary" ${puedeVerFicha ? 'role="button" tabindex="0" title="Ver ficha del cliente"' : ""}>
                                ${renderClienteAvatar(cliente, "chat-contact-avatar")}
                                <div class="chat-contact-copy">
                                <div class="chat-name">${escapeHtml(cliente.nombre || "Sin nombre")}</div>
                                <div class="chat-phone" title="${escapeAttribute(detalleContacto)}">${escapeHtml(detalleContacto)}</div>
                                </div>
                            </div>
                            <div class="chat-channel-actions">
                                ${puedeSolicitarReasignacion ? `<button id="requestReassignmentButton" class="chat-transfer-button" type="button" title="Solicitar transferencia">
                                    <i data-lucide="user-round-cog"></i><span>Transferir</span>
                                </button>` : ""}
                                ${puedeControlarBot ? `<button id="chatBotToggle" class="chat-bot-toggle ${botActivo ? "active" : "paused"}" type="button" title="${botActivo ? "Bot activo" : "Bot pausado"}">
                                    <span class="bot-toggle-dot"></span>
                                    <span>${botActivo ? "Bot activo" : "Bot pausado"}</span>
                                </button>` : ""}
                                ${contieneComentarios ? '<span class="chat-public-comment-badge" title="Esta conversación contiene comentarios públicos"><i data-lucide="message-square-text"></i><span>Comentario público</span></span>' : ""}
                                <span class="chat-channel-badge" role="img" title="${escapeAttribute(red.nombre)}" aria-label="${escapeAttribute(red.nombre)}">${crearLogoRed(red.clase)}</span>
                                ${puedeVerFicha ? `<button id="toggleCustomerDetails" class="chat-details-toggle" type="button"
                                    title="${fichaClienteColapsada ? "Mostrar ficha del cliente" : "Ocultar ficha del cliente"}"
                                    aria-label="${fichaClienteColapsada ? "Mostrar ficha del cliente" : "Ocultar ficha del cliente"}"
                                    aria-expanded="${!fichaClienteColapsada}">
                                    <i data-lucide="${fichaClienteColapsada ? "panel-right-open" : "panel-right-close"}"></i>
                                </button>` : ""}
                            </div>
                        </div>`;
            document.getElementById("mobileChatBack")?.addEventListener("click", () => {
                if (modoDetalleConversacion) {
                    abrirModulo(moduloRetornoDetalle);
                    return;
                }
                document.body.classList.remove("mobile-chat-open");
                document.body.classList.remove("mobile-contact-details-open");
                if (!window.matchMedia("(max-width: 1100px)").matches) {
                    limpiarConversacionSeleccionada();
                    mostrarConversaciones();
                }
            });
            const abrirFichaDesdeChat = () => {
                if (!puedeVerFicha) return;
                document.body.classList.add("mobile-contact-details-open");
                mostrarFichaCliente(conversacion);
            };
            document.querySelector(".chat-contact-summary")?.addEventListener("click", abrirFichaDesdeChat);
            document.querySelector(".chat-contact-summary")?.addEventListener("keydown", event => {
                if (event.key === "Enter" || event.key === " ") { event.preventDefault(); abrirFichaDesdeChat(); }
            });
            if (window.lucide) window.lucide.createIcons();
            document.getElementById("leadDetailId").textContent =
                `Conversación #${conversacion.id} · ${cliente.nombre || "Sin nombre"}`;
            document.getElementById("chatBotToggle")?.addEventListener("click", () => {
                cambiarBotConversacion(conversacion.id, botActivo ? "PAUSADO" : "ACTIVO");
            });
            document.getElementById("requestReassignmentButton")?.addEventListener("click", () => {
                solicitarReasignacionDesdeChat(conversacion.id);
            });
            document.getElementById("toggleCustomerDetails")?.addEventListener("click", alternarFichaCliente);
            document.querySelector(".main")?.classList.toggle("customer-details-collapsed", !puedeVerFicha || fichaClienteColapsada);
            renderizarMensajesConversacion(conversacion.mensajes || [], { conservarScroll: opciones.conservarScroll });
            renderizarPlantillasRapidas();
            const puedeEnviar = tienePermiso("mensajes.enviar") && conversacion.puedeAtender !== false;
            input.disabled = !puedeEnviar;
            boton.disabled = !puedeEnviar;
            attachButton.disabled = !puedeEnviar;
            input.placeholder = !puedeEnviar
                ? "Solo lectura: solicita permiso para enviar mensajes"
                : contieneComentarios
                    ? "Mensaje privado al contacto (no responde el comentario público)"
                    : "Escribir mensaje...";
            if (refrescarFicha && puedeVerFicha) {
                mostrarFichaCliente(conversacion);
            }
        }

        async function reasignarConversacionDesdeChat(conversacionId, usuarioId) {
            const response = await api(`/api/crm/conversaciones/${conversacionId}/asignar`, {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ usuarioId: usuarioId ? Number(usuarioId) : null })
            });
            if (!response.ok) {
                notificar(await obtenerMensajeError(response, "No se pudo transferir la conversación."), "error");
                await seleccionarConversacion(conversacionId);
                return;
            }

            await cargarConversaciones();
            await seleccionarConversacion(conversacionId);
            notificar("Conversación transferida.", "success");
        }

        async function solicitarReasignacionDesdeChat(conversacionId) {
            if (!window.confirm("La conversación quedará sin asesor y disponible para reasignación. ¿Continuar?")) return;

            const response = await api(`/api/crm/conversaciones/${conversacionId}/solicitar-reasignacion`, {
                method: "POST"
            });
            if (!response.ok) {
                notificar(await obtenerMensajeError(response, "No se pudo solicitar la transferencia."), "error");
                return;
            }

            const resultado = await response.json();
            limpiarConversacionSeleccionada();
            await cargarConversaciones();
            notificar(resultado.mensaje || "Transferencia solicitada.", "success");
        }

        function alternarFichaCliente() {
            if (window.matchMedia("(max-width: 1100px)").matches) {
                document.body.classList.toggle("mobile-contact-details-open");
                return;
            }

            fichaClienteColapsada = !fichaClienteColapsada;
            document.querySelector(".main")?.classList.toggle("customer-details-collapsed", fichaClienteColapsada);
            const botonFicha = document.getElementById("toggleCustomerDetails");
            const etiqueta = fichaClienteColapsada ? "Mostrar ficha del cliente" : "Ocultar ficha del cliente";
            botonFicha?.setAttribute("title", etiqueta);
            botonFicha?.setAttribute("aria-label", etiqueta);
            botonFicha?.setAttribute("aria-expanded", String(!fichaClienteColapsada));
            if (botonFicha && window.lucide) {
                botonFicha.innerHTML = `<i data-lucide="${fichaClienteColapsada ? "panel-right-open" : "panel-right-close"}"></i>`;
                window.lucide.createIcons();
            }
        }

        async function obtenerPlantillasRapidas() {
            if (plantillasRapidasCache) return plantillasRapidasCache;

            try {
                const response = await api("/api/plantillas-rapidas");
                if (!response.ok) throw new Error("No se pudieron cargar las plantillas.");
                const data = await response.json();
                plantillasRapidasCache = Array.isArray(data.templates)
                    ? data.templates.filter(template => template.message)
                    : [];
            } catch (error) {
                console.error(error);
                plantillasRapidasCache = [];
            }

            return plantillasRapidasCache;
        }

        async function renderizarPlantillasRapidas() {
            if (!quickReplies) return;
            if (!conversacionSeleccionada) {
                quickReplies.classList.add("hidden");
                quickReplies.innerHTML = "";
                return;
            }

            const plantillas = await obtenerPlantillasRapidas();
            if (!plantillas.length) {
                quickReplies.classList.add("hidden");
                quickReplies.innerHTML = "";
                return;
            }

            quickReplies.innerHTML = `
                <div class="quick-replies-list">
                    ${plantillas.slice(0, 10).map(template => `
                        <button type="button" class="quick-reply" data-quick-reply="${escapeAttribute(template.message)}" title="${escapeAttribute(template.message)}">
                            ${escapeHtml(template.title || "Respuesta")}
                        </button>
                    `).join("")}
                </div>`;
            quickReplies.classList.remove("hidden");
            quickReplies.querySelectorAll("[data-quick-reply]").forEach(button => {
                button.addEventListener("click", () => {
                    input.value = button.dataset.quickReply || "";
                    input.focus();
                });
            });
        }

        async function actualizarPerfilMeta(id) {
            try {
                const response = await api(`/api/crm/conversaciones/${id}/actualizar-perfil-meta`, {
                    method: "POST"
                });
                const data = await response.json().catch(() => ({}));
                if (!response.ok || !data.success) {
                    notificar(data.error || "Meta no devolvió el nombre del contacto.", "error");
                    return;
                }

                notificar(`Nombre actualizado: ${escapeHtml(data.nombre || "contacto")}`, "success");
                await cargarConversaciones();
                await seleccionarConversacion(id);
            } catch (error) {
                console.error(error);
                notificar("No se pudo actualizar el nombre desde Meta.", "error");
            }
        }

        function renderizarMensajesConversacion(listaMensajes, opciones = {}) {
            const scrollAnterior = mensajes.scrollTop;
            const estabaAlFinal = mensajes.scrollHeight - mensajes.clientHeight - scrollAnterior < 60;
            mensajes.innerHTML = "";
            if (conversacionSeleccionada?.hayMasMensajes) {
                mensajes.innerHTML = '<button type="button" id="loadPreviousMessages" class="secondary-btn history-load">Mensajes anteriores</button>';
                mensajes.querySelector("#loadPreviousMessages").addEventListener("click", cargarMensajesAnteriores);
            }
            listaMensajes.forEach(mensaje => {
                const elemento = document.createElement("div");
                const entrante = mensaje.direccion === "E";
                const tipoMensaje = String(mensaje.tipo || "").toLowerCase();
                const esImagen = tipoMensaje === "image" || tipoMensaje === "sticker";
                const esSticker = tipoMensaje === "sticker";
                const esAudio = tipoMensaje === "audio" || tipoMensaje === "voice";
                const esComentario = tipoMensaje === "comment";
                elemento.className = `message ${entrante ? "incoming" : "outgoing"}${esImagen ? " has-image" : ""}${esSticker ? " has-sticker" : ""}${esAudio ? " has-audio" : ""}${esComentario ? " public-comment" : ""}`;
                const ticks = entrante ? "" : crearTicksMensaje(mensaje);
                const autor = esComentario ? "Comentario público" : (entrante ? "Cliente" : (mensaje.tipo === "bot" ? "Bot" : "CRM"));
                elemento.dataset.messageId = mensaje.id;
                const puedeResponder = String(conversacionSeleccionada?.canal || "WHATSAPP").toUpperCase() === "WHATSAPP" &&
                    Boolean(mensaje.externalId || mensaje.whatsappId);
                elemento.innerHTML = crearCitaMensaje(mensaje.respuestaA) + crearContenidoMensaje(mensaje) +
                    (puedeResponder ? `<button type="button" class="message-reply-button" data-reply-message="${mensaje.id}" title="Responder este mensaje" aria-label="Responder este mensaje"><i data-lucide="reply"></i></button>` : "") +
                    `<div class="message-info"><span>${autor} · ${formatearFecha(mensaje.fecha)}</span>${ticks}</div>`;
                mensajes.appendChild(elemento);
            });
            mensajes.querySelectorAll("[data-reply-message]").forEach(botonRespuesta => {
                botonRespuesta.addEventListener("click", event => {
                    event.stopPropagation();
                    const mensaje = listaMensajes.find(item => String(item.id) === botonRespuesta.dataset.replyMessage);
                    seleccionarMensajeParaResponder(mensaje);
                });
            });
            mensajes.querySelectorAll("[data-scroll-message]").forEach(cita => {
                cita.addEventListener("click", () => {
                    const original = mensajes.querySelector(`[data-message-id="${CSS.escape(cita.dataset.scrollMessage)}"]`);
                    original?.scrollIntoView({ behavior: "smooth", block: "center" });
                    original?.classList.add("message-highlight");
                    window.setTimeout(() => original?.classList.remove("message-highlight"), 1200);
                });
            });
            mensajes.querySelectorAll("[data-image-preview]").forEach(botonImagen => {
                botonImagen.addEventListener("click", () => {
                    abrirVistaPreviaImagen(
                        botonImagen.dataset.imagePreview,
                        botonImagen.dataset.imageAlt,
                        botonImagen);
                });
            });
            if (window.lucide) window.lucide.createIcons();
            inicializarReproductoresAudio(mensajes);
            mensajes.scrollTop = opciones.conservarScroll && !estabaAlFinal ? scrollAnterior : mensajes.scrollHeight;
        }

        async function cargarMensajesAnteriores() {
            const conversacion = conversacionSeleccionada;
            if (!conversacion?.hayMasMensajes) return;
            const version = seleccionConversacionVersion;
            const button = mensajes.querySelector("#loadPreviousMessages");
            button.disabled = true;
            try {
                const response = await api(`/api/whatsapp/conversaciones/${conversacion.id}?antesDe=${conversacion.mensajeMasAntiguoId}&pageSize=50`);
                if (!response.ok) throw new Error("No se pudo cargar el historial.");
                const data = await response.json();
                if (version !== seleccionConversacionVersion || conversacionSeleccionada !== conversacion) return;
                const height = mensajes.scrollHeight;
                const top = mensajes.scrollTop;
                const byId = new Map([...(data.mensajes || []), ...conversacion.mensajes].map(m => [String(m.id), m]));
                conversacion.mensajes = [...byId.values()].sort((a, b) => new Date(a.fecha) - new Date(b.fecha) || Number(a.id) - Number(b.id));
                conversacion.hayMasMensajes = data.hayMasMensajes;
                conversacion.mensajeMasAntiguoId = data.mensajeMasAntiguoId;
                renderizarMensajesConversacion(conversacion.mensajes, { conservarScroll: true });
                mensajes.scrollTop = top + mensajes.scrollHeight - height;
            } catch (error) {
                if (error?.name !== "AbortError") notificar(error.message, "error");
            } finally {
                if (button.isConnected) button.disabled = false;
            }
        }

        function resumenMensajeParaCita(mensaje) {
            if (!mensaje) return "Mensaje";
            const tipo = String(mensaje.tipo || "text").toLowerCase();
            if (tipo === "text" || tipo === "bot") return String(mensaje.mensaje || "Mensaje").slice(0, 140);
            const nombres = { image: "Imagen", sticker: "Sticker", audio: "Audio", voice: "Audio", video: "Video", document: "Documento", "application/pdf": "Documento PDF" };
            return nombres[tipo] || "Archivo adjunto";
        }

        function crearCitaMensaje(respuestaA) {
            if (!respuestaA) return "";
            const autor = respuestaA.direccion === "E" ? "Cliente" : "Tú";
            return `<button type="button" class="message-quote" data-scroll-message="${respuestaA.id}">
                        <strong>${autor}</strong><span>${escapeHtml(resumenMensajeParaCita(respuestaA))}</span>
                    </button>`;
        }

        function seleccionarMensajeParaResponder(mensaje) {
            if (!mensaje || !replyPreview) return;
            mensajeRespuestaSeleccionado = mensaje;
            const autor = mensaje.direccion === "E" ? "Cliente" : "Tú";
            replyPreview.innerHTML = `<div><strong>Responder a ${autor}</strong><span>${escapeHtml(resumenMensajeParaCita(mensaje))}</span></div>
                <button type="button" id="cancelReplyButton" aria-label="Cancelar respuesta"><i data-lucide="x"></i></button>`;
            replyPreview.classList.remove("hidden");
            document.getElementById("cancelReplyButton")?.addEventListener("click", limpiarRespuestaSeleccionada);
            if (window.lucide) window.lucide.createIcons();
            input.focus();
        }

        function limpiarRespuestaSeleccionada() {
            mensajeRespuestaSeleccionado = null;
            if (!replyPreview) return;
            replyPreview.innerHTML = "";
            replyPreview.classList.add("hidden");
        }

        function agregarMensajeOptimista(texto, respuestaA = null) {
            if (!conversacionSeleccionada) return null;

            const mensajeTemporal = {
                id: `tmp-${Date.now()}`,
                direccion: "S",
                tipo: "text",
                estado: "ENVIANDO",
                mensaje: texto,
                respuestaA: respuestaA ? {
                    id: respuestaA.id,
                    direccion: respuestaA.direccion,
                    tipo: respuestaA.tipo,
                    mensaje: respuestaA.mensaje
                } : null,
                fecha: new Date().toISOString()
            };

            conversacionSeleccionada.mensajes = [
                ...(conversacionSeleccionada.mensajes || []),
                mensajeTemporal
            ];
            conversacionSeleccionada.ultimoMensaje = mensajeTemporal.fecha;
            renderizarMensajesConversacion(conversacionSeleccionada.mensajes);
            return mensajeTemporal.id;
        }

        function crearContenidoMensaje(mensaje) {
            const tipo = String(mensaje.tipo || "").toLowerCase();
            if (tipo === "comment") {
                const texto = String(mensaje.mensaje || "").replace(/^Comentario:\s*/i, "");
                return `<div class="public-comment-label"><i data-lucide="message-square-text"></i><span>Comentario en publicación</span></div><div>${escapeHtml(texto)}</div>`;
            }
            const esAdjunto = ["image", "sticker", "document", "application/pdf", "audio", "voice", "video"].includes(tipo);
            if (!esAdjunto) {
                return `<div>${escapeHtml(mensaje.mensaje || "")}</div>`;
            }

            let archivo;
            try {
                archivo = JSON.parse(mensaje.mensaje);
            } catch {
                const archivoAntiguo = String(mensaje.mensaje || "");
                if (["image", "sticker"].includes(tipo) || /\.(jpe?g|png|webp)(\?|$)/i.test(archivoAntiguo)) {
                    return crearBotonVistaPreviaImagen(
                        archivoAntiguo,
                        tipo === "sticker" ? "Sticker" : "Imagen",
                        tipo === "sticker");
                }
                // Mensajes antiguos guardados antes de tener el JSON estructurado
                return `<a href="${escapeAttribute(archivoAntiguo)}" target="_blank" rel="noopener">Abrir documento</a>`;
            }

            const urlArchivo = Number.isFinite(Number(mensaje.id))
                ? `/api/archivos/mensajes/${encodeURIComponent(mensaje.id)}`
                : archivo.url;
            const esSticker = tipo === "sticker";
            const esImagen = tipo === "image" || esSticker || /\.(jpe?g|png|webp)$/i.test(archivo.nombre || archivo.url || "");

            if (esImagen) {
                return crearBotonVistaPreviaImagen(
                    urlArchivo,
                    archivo.nombre || (esSticker ? "Sticker" : "Imagen"),
                    esSticker);
            }

            if (tipo === "audio" || tipo === "voice") {
                return `<div class="audio-message">
                            <button class="audio-play-button" type="button" aria-label="Reproducir audio" aria-pressed="false"><span class="audio-play-icon"></span></button>
                            <div class="audio-controls">
                                <input class="audio-seek" type="range" min="0" max="0" step="0.1" value="0" aria-label="Progreso del audio">
                                <div class="audio-times"><span data-audio-current>0:00</span><span data-audio-duration>--:--</span></div>
                            </div>
                            <audio preload="metadata" src="${escapeAttribute(urlArchivo)}"></audio>
                        </div>`;
            }

            if (tipo === "video") {
                return `<video class="message-media-player" controls preload="metadata" src="${escapeAttribute(urlArchivo)}"></video>`;
            }

            const esPdf = String(archivo.mimeType || "").toLowerCase() === "application/pdf" || /\.pdf$/i.test(archivo.nombre || "");
            const nombreArchivo = String(archivo.nombre || "Documento");
            const nombreSeguro = escapeHtml(nombreArchivo).replace(/([._-])/g, "$1<wbr>");
            const extension = nombreArchivo.match(/\.([^.]+)$/)?.[1]?.toUpperCase() || "ARCHIVO";
            const tamano = formatearTamanoAdjunto(archivo.tamano);
            return `<a href="${escapeAttribute(urlArchivo)}" target="_blank" rel="noopener" class="file-card">
                        <span class="file-icon" aria-hidden="true"><i data-lucide="${esPdf ? "file-text" : "file"}"></i></span>
                        <span class="file-card-copy"><strong class="file-card-name" title="${escapeAttribute(nombreArchivo)}">${nombreSeguro}</strong><small>${escapeHtml(extension)}${tamano ? ` · ${tamano}` : ""}</small></span>
                    </a>`;
        }

        function formatearTamanoAdjunto(bytes) {
            const tamano = Number(bytes);
            if (!Number.isFinite(tamano) || tamano <= 0) return "";
            if (tamano < 1024 * 1024) return `${Math.max(1, Math.round(tamano / 1024))} KB`;
            return `${(tamano / (1024 * 1024)).toFixed(1)} MB`;
        }

        function crearBotonVistaPreviaImagen(url, alt, esSticker = false) {
            return `<button type="button" class="image-card image-preview-trigger ${esSticker ? "sticker-card" : ""}"
                        data-image-preview="${escapeAttribute(url)}"
                        data-image-alt="${escapeAttribute(alt)}"
                        aria-label="Ver ${escapeAttribute(alt)} en grande">
                        <img src="${escapeAttribute(url)}" alt="${escapeAttribute(alt)}" loading="lazy" decoding="async" onerror="this.closest('.image-preview-trigger')?.classList.add('media-load-error'); this.alt='No se pudo cargar el archivo';">
                    </button>`;
        }

        function abrirVistaPreviaImagen(url, alt, elementoOrigen) {
            if (!url || !modalHost) return;

            const cerrarVistaPrevia = () => {
                document.removeEventListener("keydown", cerrarConEscape);
                cerrarModal();
                elementoOrigen?.focus({ preventScroll: true });
            };
            const cerrarConEscape = event => {
                if (event.key === "Escape") cerrarVistaPrevia();
            };

            modalHost.innerHTML = `
                <div class="image-preview-backdrop"></div>
                <section class="image-preview-dialog" role="dialog" aria-modal="true" aria-label="${escapeAttribute(alt || "Vista previa de imagen")}">
                    <button class="image-preview-close" type="button" aria-label="Cerrar vista previa">
                        <i data-lucide="x"></i>
                    </button>
                    <img src="${escapeAttribute(url)}" alt="${escapeAttribute(alt || "Imagen")}">
                </section>`;
            modalHost.classList.remove("hidden");
            modalHost.setAttribute("aria-hidden", "false");
            modalHost.querySelector(".image-preview-backdrop")?.addEventListener("click", cerrarVistaPrevia);
            modalHost.querySelector(".image-preview-close")?.addEventListener("click", cerrarVistaPrevia);
            document.addEventListener("keydown", cerrarConEscape);
            if (window.lucide) window.lucide.createIcons();
            modalHost.querySelector(".image-preview-close")?.focus();
        }

        function inicializarReproductoresAudio(contenedor) {
            contenedor.querySelectorAll(".audio-message").forEach(reproductor => {
                const audio = reproductor.querySelector("audio");
                const boton = reproductor.querySelector(".audio-play-button");
                const barra = reproductor.querySelector(".audio-seek");
                const tiempoActual = reproductor.querySelector("[data-audio-current]");
                const duracion = reproductor.querySelector("[data-audio-duration]");
                if (!audio || !boton || !barra || !tiempoActual || !duracion) return;

                const actualizarTiempo = () => {
                    const total = Number.isFinite(audio.duration) ? audio.duration : 0;
                    barra.max = String(total);
                    barra.value = String(audio.currentTime || 0);
                    barra.style.setProperty(
                        "--audio-progress",
                        `${total > 0 ? (audio.currentTime / total) * 100 : 0}%`);
                    tiempoActual.textContent = formatearTiempoAudio(audio.currentTime);
                    duracion.textContent = formatearTiempoAudio(total);
                };
                const actualizarEstado = () => {
                    const reproduciendo = !audio.paused && !audio.ended;
                    boton.setAttribute("aria-pressed", String(reproduciendo));
                    boton.setAttribute("aria-label", reproduciendo ? "Pausar audio" : "Reproducir audio");
                    reproductor.classList.toggle("playing", reproduciendo);
                };

                boton.addEventListener("click", () => {
                    if (audio.paused) {
                        contenedor.querySelectorAll(".audio-message audio").forEach(otroAudio => {
                            if (otroAudio !== audio) otroAudio.pause();
                        });
                        audio.play().catch(error => console.error("No se pudo reproducir el audio", error));
                    } else {
                        audio.pause();
                    }
                });
                barra.addEventListener("input", () => {
                    audio.currentTime = Number(barra.value) || 0;
                    actualizarTiempo();
                });
                audio.addEventListener("loadedmetadata", actualizarTiempo);
                audio.addEventListener("durationchange", actualizarTiempo);
                audio.addEventListener("timeupdate", actualizarTiempo);
                audio.addEventListener("play", actualizarEstado);
                audio.addEventListener("pause", actualizarEstado);
                audio.addEventListener("ended", () => {
                    actualizarEstado();
                    actualizarTiempo();
                });
                actualizarTiempo();
                actualizarEstado();
            });
        }

        function formatearTiempoAudio(segundos) {
            const total = Number.isFinite(segundos) ? Math.max(0, Math.floor(segundos)) : 0;
            return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, "0")}`;
        }

        // =========================================================
        // CHECKS DE ESTADO DEL MENSAJE (estilo WhatsApp)
        // =========================================================
        //
        // ✓ gris       -> ENVIADO (salió de Meta, aún no llega al cliente)
        // ✓✓ gris      -> ENTREGADO (llegó al teléfono del cliente)
        // ✓✓ azul      -> LEIDO (el cliente abrió el mensaje)
        // ✗ rojo       -> FALLIDO (Meta no pudo entregarlo)
        //
        // Estos estados los actualiza el backend cuando Meta envía
        // los acuses "statuses" al webhook.
        //
        // =========================================================

        function crearTicksMensaje(mensaje) {
            const estado = (mensaje.estado || "").toUpperCase();

            if (estado === "ENVIANDO") {
                return `<span class="message-ticks tick-pendiente" title="Enviando...">...</span>`;
            }
            if (estado === "ERROR") {
                return `<span class="message-ticks tick-fallido" title="No se pudo enviar">&#10007;</span>`;
            }
            if (estado.startsWith("FALLIDO")) {
                const detalle = estado.includes(":") ? estado.split(":").slice(1).join(":") : "";
                const titulo = detalle ? `No se pudo entregar: ${detalle}` : "No se pudo entregar";
                return `<span class="message-ticks tick-fallido" title="${escapeAttribute(titulo)}">&#10007;</span>`;
            }
            if (estado === "LEIDO") {
                return `<span class="message-ticks tick-leido" title="Leído">&#10003;&#10003;</span>`;
            }
            if (estado === "ENTREGADO") {
                return `<span class="message-ticks tick-entregado" title="Entregado">&#10003;&#10003;</span>`;
            }
            if (estado === "ENVIADO") {
                return `<span class="message-ticks tick-enviado" title="Enviado, aún no entregado">&#10003;</span>`;
            }
            if (estado === "LOCAL") {
                return `<span class="message-ticks tick-pendiente" title="Guardado en CRM. No enviado por WhatsApp porque el archivo no tiene URL pública HTTPS.">&#128206;</span>`;
            }

            // Mensajes de prueba (sin Meta conectada) u otros estados
            // que no vienen de un acuse todavía.
            return `<span class="message-ticks tick-pendiente" title="Enviado">&#10003;</span>`;
        }
