// Módulo frontend del CRM.

        function formatearFecha(fecha) {
            if (!fecha) return "";
            return new Date(fecha).toLocaleString("es-PE", {
                day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit"
            });
        }

        function formatearMoneda(valor, moneda = "PEN") {
            return new Intl.NumberFormat("es-PE", {
                style: "currency",
                currency: moneda
            }).format(Number(valor || 0));
        }

        function formatearNumero(valor) {
            return new Intl.NumberFormat("es-PE").format(Number(valor || 0));
        }

        const tamanosPaginacion = new Map();
        const opcionesTamanoPagina = [10, 25, 50, 100];

        function obtenerTamanoPagina(clave) {
            if (tamanosPaginacion.has(clave)) return tamanosPaginacion.get(clave);
            let valor;
            try { valor = Number(localStorage.getItem(`crm.pageSize.${clave}`)); } catch { /* Storage can be unavailable. */ }
            const size = opcionesTamanoPagina.includes(valor) ? valor : 10;
            tamanosPaginacion.set(clave, size);
            return size;
        }

        function renderPaginacion(pagina, clave = "general") {
            const total = Math.max(0, Number(pagina.total || 0));
            const size = Number(pagina.pageSize || obtenerTamanoPagina(clave));
            const pages = Math.max(1, Math.ceil(total / size));
            const page = Math.max(1, Number(pagina.page || 1));
            const desde = total ? Math.min(total, (page - 1) * size + 1) : 0;
            const hasta = Math.min(total, page * size);
            const opciones = [...new Set([...opcionesTamanoPagina, size])].sort((a, b) => a - b);
            const control = (action, destino, icono, label, disabled) =>
                `<button type="button" class="table-icon-action" data-pagination-action="${action}" data-page="${destino}" ${disabled ? "disabled" : ""} title="${label}" aria-label="${label}"><i data-lucide="${icono}"></i></button>`;
            return `<nav class="list-pagination" aria-label="Paginacion">
                <label class="list-pagination-size"><span>Elementos por página</span><select data-page-size data-list-key="${escapeAttribute(clave)}">${opciones.map(value => `<option value="${value}" ${value === size ? "selected" : ""}>${value}</option>`).join("")}</select></label>
                <div class="list-pagination-navigation">
                    <span class="list-pagination-range" aria-live="polite">${formatearNumero(desde)}-${formatearNumero(hasta)} de ${formatearNumero(total)}</span>
                    <div class="list-pagination-buttons">
                        ${control("first", 1, "skip-back", "Primera pagina", page <= 1)}
                        ${control("previous", page - 1, "chevron-left", "Pagina anterior", page <= 1)}
                        ${control("next", page + 1, "chevron-right", "Pagina siguiente", page >= pages)}
                        ${control("last", pages, "skip-forward", "Ultima pagina", page >= pages)}
                    </div>
                </div>
            </nav>`;
        }

        function enlazarPaginacion(vista, cargar) {
            const ejecutar = async (control, page) => {
                const pager = control.closest(".list-pagination");
                if (pager.getAttribute("aria-busy") === "true") return;
                const habilitados = [...pager.querySelectorAll("button, select")].filter(element => !element.disabled);
                pager.setAttribute("aria-busy", "true");
                habilitados.forEach(element => element.disabled = true);
                try { await cargar(page); }
                catch (error) {
                    if (error?.name !== "AbortError") notificar("No se pudo cargar la pagina.", "error");
                } finally {
                    pager.removeAttribute("aria-busy");
                    habilitados.forEach(element => element.disabled = false);
                }
            };
            vista.querySelectorAll(".list-pagination [data-page]").forEach(button => {
                button.addEventListener("click", () => ejecutar(button, Number(button.dataset.page)));
            });
            vista.querySelectorAll(".list-pagination [data-page-size]").forEach(selector => {
                selector.addEventListener("change", () => {
                    const size = Number(selector.value);
                    const clave = selector.dataset.listKey;
                    if (!opcionesTamanoPagina.includes(size)) return;
                    tamanosPaginacion.set(clave, size);
                    try { localStorage.setItem(`crm.pageSize.${clave}`, String(size)); } catch { /* Keep the session preference. */ }
                    ejecutar(selector, 1);
                });
            });
            window.lucide?.createIcons(vista);
        }

        function prepararTablasResponsivas(raiz = document) {
            const tablas = [];
            if (raiz instanceof Element && raiz.matches("table.module-table")) tablas.push(raiz);
            raiz.querySelectorAll?.("table.module-table").forEach(tabla => tablas.push(tabla));

            tablas.forEach(tabla => {
                const encabezados = [...tabla.querySelectorAll("thead th")]
                    .map(celda => celda.textContent.trim());

                tabla.querySelectorAll("tbody tr").forEach(fila => {
                    const celdas = [...fila.children].filter(celda => celda.tagName === "TD");
                    const esFilaVacia = celdas.length === 1 && Number(celdas[0].colSpan || 1) > 1;
                    fila.classList.toggle("responsive-empty-row", esFilaVacia);

                    celdas.forEach((celda, indice) => {
                        if (esFilaVacia) {
                            celda.removeAttribute("data-label");
                            return;
                        }
                        celda.dataset.label = encabezados[indice] || `Campo ${indice + 1}`;
                    });
                });

                tabla.classList.add("responsive-table-ready");
                if (!tabla.classList.contains("contacts-table")) crearTarjetasTablaMovil(tabla, encabezados);
            });
        }

        function crearTarjetasTablaMovil(tabla, encabezados) {
            tabla.parentElement?.querySelector(`.table-mobile-list[data-table-mobile-id="${tabla.dataset.mobileTableId || ""}"]`)?.remove();
            const identificador = tabla.dataset.mobileTableId || `table-${Date.now()}-${Math.random().toString(16).slice(2)}`;
            tabla.dataset.mobileTableId = identificador;

            const lista = document.createElement("div");
            lista.className = "table-mobile-list";
            lista.dataset.tableMobileId = identificador;
            lista.setAttribute("aria-label", "Vista móvil de la tabla");

            const filas = [...tabla.querySelectorAll("tbody tr")];
            filas.forEach(fila => {
                const celdas = [...fila.children].filter(celda => celda.tagName === "TD");
                const esFilaVacia = celdas.length === 1 && Number(celdas[0].colSpan || 1) > 1;
                if (esFilaVacia) {
                    const vacio = document.createElement("div");
                    vacio.className = "table-mobile-empty";
                    vacio.textContent = celdas[0].textContent.trim() || "No hay información.";
                    lista.append(vacio);
                    return;
                }

                const indicePrincipal = ["Nombre", "Asesor", "Usuario", "Estado", "Etapa", "Fecha", "Publicación"]
                    .map(nombre => encabezados.findIndex(encabezado => encabezado.toLowerCase() === nombre.toLowerCase()))
                    .find(indice => indice >= 0) ?? 0;
                const titulo = celdas[indicePrincipal]?.textContent.trim() || "Detalle";
                const indiceSecundario = celdas.findIndex((celda, indice) =>
                    indice !== indicePrincipal &&
                    !/acciones/i.test(encabezados[indice] || "") &&
                    celda.textContent.trim());
                const subtitulo = indiceSecundario >= 0 ? celdas[indiceSecundario].textContent.trim() : "";
                const encabezadoPrincipal = encabezados[indicePrincipal] || "";
                const icono = /nombre|asesor|usuario/i.test(encabezadoPrincipal) ? "user-round" :
                    /fecha/i.test(encabezadoPrincipal) ? "calendar-range" :
                    /publicación/i.test(encabezadoPrincipal) ? "image-up" : "file-text";

                const tarjeta = document.createElement("article");
                tarjeta.className = "table-mobile-card";
                tarjeta.innerHTML = `
                    <span class="table-mobile-avatar" aria-hidden="true"><i data-lucide="${icono}"></i></span>
                    <span class="table-mobile-summary">
                        <strong>${escapeHtml(titulo)}</strong>
                        ${subtitulo && subtitulo !== titulo ? `<small>${escapeHtml(subtitulo)}</small>` : ""}
                    </span>
                    <button type="button" class="secondary-btn table-mobile-view"><i data-lucide="eye" aria-hidden="true"></i><span>Ver</span></button>`;
                tarjeta.querySelector(".table-mobile-view").addEventListener("click", () => abrirDetalleTablaMovil(tabla, fila, encabezados, titulo, icono));
                lista.append(tarjeta);
            });

            tabla.insertAdjacentElement("afterend", lista);
            window.lucide?.createIcons(lista);
        }

        function abrirDetalleTablaMovil(tabla, fila, encabezados, titulo, icono) {
            const celdas = [...fila.children].filter(celda => celda.tagName === "TD");
            const detalles = celdas.map((celda, indice) => {
                const etiqueta = encabezados[indice] || `Campo ${indice + 1}`;
                if (/acciones/i.test(etiqueta)) return "";
                const enlace = celda.querySelector('a[href^="https://"]');
                const valor = celda.textContent.trim() || "—";
                const contenido = enlace
                    ? `<a href="${escapeAttribute(enlace.href)}" target="_blank" rel="noopener noreferrer">${escapeHtml(valor)}</a>`
                    : `<strong>${escapeHtml(valor)}</strong>`;
                return `<div><span>${escapeHtml(etiqueta)}</span>${contenido}</div>`;
            }).join("");
            const accionesOriginales = [...fila.querySelectorAll("button")];
            const acciones = accionesOriginales.map((boton, indice) => {
                const nombre = boton.getAttribute("aria-label") || boton.getAttribute("title") || boton.textContent.trim() || `Acción ${indice + 1}`;
                return `<button type="button" class="secondary-btn" data-mobile-table-action="${indice}">${escapeHtml(nombre)}</button>`;
            }).join("");

            modalHost.innerHTML = `
                <div class="modal-backdrop" data-mobile-table-close></div>
                <section class="crm-modal table-detail-modal" role="dialog" aria-modal="true" aria-labelledby="mobileTableDetailTitle">
                    <div class="crm-modal-head table-detail-head">
                        <span class="table-mobile-avatar" aria-hidden="true"><i data-lucide="${icono}"></i></span>
                        <div><span class="contact-detail-eyebrow">Detalle</span><h2 id="mobileTableDetailTitle">${escapeHtml(titulo)}</h2></div>
                        <button type="button" class="modal-close" data-mobile-table-close aria-label="Cerrar">×</button>
                    </div>
                    <div class="table-detail-facts">${detalles}</div>
                    <div class="table-detail-actions">
                        ${acciones}
                        <button type="button" class="secondary-btn" data-mobile-table-close>Cerrar</button>
                    </div>
                </section>`;
            modalHost.classList.remove("hidden");
            modalHost.setAttribute("aria-hidden", "false");
            modalHost.querySelectorAll("[data-mobile-table-close]").forEach(elemento => elemento.addEventListener("click", cerrarModal));
            modalHost.querySelectorAll("[data-mobile-table-action]").forEach(boton => {
                boton.addEventListener("click", () => {
                    const accionOriginal = accionesOriginales[Number(boton.dataset.mobileTableAction)];
                    cerrarModal();
                    accionOriginal?.click();
                });
            });
            window.lucide?.createIcons(modalHost);
            modalHost.querySelector("[data-mobile-table-close]")?.focus();
        }

        function escapeHtml(text) {
            const div = document.createElement("div");
            div.textContent = text == null ? "" : text;
            return div.innerHTML;
        }

        function escapeAttribute(text) {
            return escapeHtml(text).replace(/"/g, "&quot;");
        }

        async function obtenerMensajeError(response, fallback) {
            const texto = await response.text();
            return texto?.trim() || `${fallback} HTTP ${response.status}`;
        }

        function notificar(mensaje, tipo = "info", accion = null, permitirHtml = false) {
            const host = document.getElementById("toastHost");
            if (!host) {
                console.log(mensaje);
                return;
            }
            const toast = document.createElement("div");
            toast.className = `toast ${tipo}`;
            if (accion) toast.classList.add("clickable");
            if (permitirHtml) {
                toast.innerHTML = mensaje;
            } else {
                toast.textContent = mensaje;
            }
            if (accion) {
                toast.addEventListener("click", () => {
                    accion();
                    toast.remove();
                });
            }
            host.appendChild(toast);
            requestAnimationFrame(() => toast.classList.add("visible"));
            setTimeout(() => {
                toast.classList.remove("visible");
                setTimeout(() => toast.remove(), 220);
            }, 3600);
        }

        function claveNotificaciones() {
            return `crm.notifications.${sesionActual?.usuario || "local"}`;
        }

        function cargarNotificacionesPersistidas() {
            try {
                const data = JSON.parse(localStorage.getItem(claveNotificaciones()) || "{}");
                notificacionesCentro = Array.isArray(data.items) ? data.items.slice(0, 20) : [];
                notificacionesNoLeidas = notificacionesCentro.filter(item => !item.leida).length;
            } catch {
                notificacionesCentro = [];
                notificacionesNoLeidas = 0;
            }
            renderizarCentroNotificaciones();
        }

        function guardarNotificacionesPersistidas() {
            try {
                localStorage.setItem(claveNotificaciones(), JSON.stringify({
                    items: notificacionesCentro.slice(0, 20)
                }));
            } catch {
                // localStorage puede estar bloqueado; la campana sigue funcionando en memoria.
            }
        }

        function registrarNotificacionMensaje(conversacion) {
            if (!conversacion) return;
            const canal = typeof obtenerCanalConversacion === "function"
                ? obtenerCanalConversacion(conversacion)
                : String(conversacion.canal || "WHATSAPP").toUpperCase();
            const red = typeof obtenerRedPorCanal === "function"
                ? obtenerRedPorCanal(canal)
                : { nombre: canal, clase: canal.toLowerCase() };
            const nombre = conversacion.nombre || "Cliente";
            const telefono = conversacion.telefono || "";
            const fecha = conversacion.ultimoMensajeCliente || conversacion.ultimoMensaje || new Date().toISOString();
            const existente = notificacionesCentro.findIndex(item => item.conversacionId === conversacion.id);
            const notificacion = {
                id: `${conversacion.id}-${Date.now()}`,
                conversacionId: conversacion.id,
                canal,
                redNombre: red.nombre,
                redClase: red.clase,
                nombre,
                detalle: telefono || "Nuevo mensaje",
                fecha,
                leida: false
            };

            if (existente >= 0) {
                notificacionesCentro.splice(existente, 1);
            }
            notificacionesCentro.unshift(notificacion);
            notificacionesCentro = notificacionesCentro.slice(0, 20);
            notificacionesNoLeidas = notificacionesCentro.filter(item => !item.leida).length;
            guardarNotificacionesPersistidas();
            renderizarCentroNotificaciones();
        }

        function registrarNotificacionSistema({ id, titulo, detalle, tipo = "SISTEMA", fecha = new Date().toISOString(), conversacionId = null, reemplazar = true }) {
            if (!id || !titulo) return;
            const existente = notificacionesCentro.findIndex(item => item.id === id);
            const notificacion = {
                id,
                conversacionId,
                canal: tipo,
                redNombre: tipo,
                redClase: tipo.toLowerCase(),
                nombre: titulo,
                detalle: detalle || "Revisar pendiente",
                fecha,
                leida: false,
                sistema: true
            };

            if (existente >= 0) {
                if (!reemplazar) return;
                notificacion.leida = notificacionesCentro[existente].leida;
                notificacionesCentro.splice(existente, 1);
            }

            notificacionesCentro.unshift(notificacion);
            notificacionesCentro = notificacionesCentro.slice(0, 20);
            notificacionesNoLeidas = notificacionesCentro.filter(item => !item.leida).length;
            guardarNotificacionesPersistidas();
            renderizarCentroNotificaciones();
        }

        function renderizarCentroNotificaciones() {
            if (!notificationBadge || !notificationList) return;
            notificationBadge.textContent = notificacionesNoLeidas > 99 ? "99+" : String(notificacionesNoLeidas);
            notificationBadge.classList.toggle("hidden", notificacionesNoLeidas === 0);

            if (!notificacionesCentro.length) {
                notificationList.innerHTML = '<div class="notification-empty">Sin notificaciones nuevas.</div>';
                return;
            }

            notificationList.innerHTML = notificacionesCentro.map(item => `
                <button type="button" class="notification-item ${item.leida ? "read" : "unread"}" data-notification-id="${escapeAttribute(item.id)}" ${item.conversacionId ? `data-notification-conversation="${item.conversacionId}"` : ""}>
                    ${item.sistema ? '<span class="notification-system-dot"></span>' : typeof crearLogoRed === "function" ? crearLogoRed(item.redClase) : ""}
                    <span class="notification-copy">
                        <strong>${escapeHtml(item.nombre)}</strong>
                        <span>${escapeHtml(item.redNombre)} · ${escapeHtml(item.detalle)}</span>
                    </span>
                    <small>${escapeHtml(formatearFecha(item.fecha))}</small>
                </button>
            `).join("");

            notificationList.querySelectorAll("[data-notification-id]").forEach(button => {
                button.addEventListener("click", async () => {
                    marcarNotificacionLeida(button.dataset.notificationId);
                    renderizarCentroNotificaciones();
                    if (button.dataset.notificationConversation) {
                        alternarCentroNotificaciones(false);
                        await abrirConversacionDesdeNotificacion(Number(button.dataset.notificationConversation));
                    }
                });
            });
        }

        function marcarNotificacionLeida(id) {
            const item = notificacionesCentro.find(notificacion => notificacion.id === id);
            if (item) item.leida = true;
            notificacionesNoLeidas = notificacionesCentro.filter(notificacion => !notificacion.leida).length;
            guardarNotificacionesPersistidas();
        }

        function marcarTodasNotificacionesLeidas() {
            notificacionesCentro.forEach(item => item.leida = true);
            notificacionesNoLeidas = 0;
            guardarNotificacionesPersistidas();
            renderizarCentroNotificaciones();
        }

        function limpiarCentroNotificaciones() {
            notificacionesCentro = [];
            notificacionesNoLeidas = 0;
            guardarNotificacionesPersistidas();
            renderizarCentroNotificaciones();
        }

        function alternarCentroNotificaciones(abierto = null) {
            if (!notificationDropdown || !notificationButton) return;
            const debeAbrir = abierto == null ? notificationDropdown.classList.contains("hidden") : abierto;
            notificationDropdown.classList.toggle("hidden", !debeAbrir);
            notificationButton.setAttribute("aria-expanded", String(debeAbrir));
        }

        function cerrarModal() {
            modalHost.classList.add("hidden");
            modalHost.setAttribute("aria-hidden", "true");
            modalHost.innerHTML = "";
        }

        function mostrarModalInfo(titulo, contenidoHtml) {
            modalHost.innerHTML = `
                <div class="modal-backdrop"></div>
                <section class="crm-modal">
                    <div class="crm-modal-head">
                        <h2>${escapeHtml(titulo)}</h2>
                        <button type="button" class="modal-close" data-modal-close aria-label="Cerrar">x</button>
                    </div>
                    <div class="crm-modal-body">${contenidoHtml}</div>
                    <div class="crm-modal-actions">
                        <button type="button" class="secondary-btn" data-modal-close>Cerrar</button>
                    </div>
                </section>`;
            modalHost.classList.remove("hidden");
            modalHost.setAttribute("aria-hidden", "false");
            modalHost.querySelectorAll("[data-modal-close], .modal-backdrop").forEach(elemento => {
                elemento.addEventListener("click", cerrarModal);
            });
        }

        function solicitarTextoModal(titulo, etiqueta, valorInicial = "") {
            return new Promise(resolve => {
                modalHost.innerHTML = `
                    <div class="modal-backdrop"></div>
                    <section class="crm-modal">
                        <form id="textPromptForm" class="crm-form">
                            <div class="crm-modal-head">
                                <h2>${escapeHtml(titulo)}</h2>
                                <button type="button" class="modal-close" data-modal-cancel aria-label="Cerrar">x</button>
                            </div>
                            <label>${escapeHtml(etiqueta)}
                                <textarea name="valor" rows="4" maxlength="1000" required>${escapeHtml(valorInicial)}</textarea>
                            </label>
                            <div class="crm-modal-actions">
                                <button type="button" class="secondary-btn" data-modal-cancel>Cancelar</button>
                                <button type="submit">Guardar</button>
                            </div>
                        </form>
                    </section>`;
                modalHost.classList.remove("hidden");
                modalHost.setAttribute("aria-hidden", "false");

                const resolver = valor => {
                    cerrarModal();
                    resolve(valor);
                };
                modalHost.querySelector("#textPromptForm").addEventListener("submit", event => {
                    event.preventDefault();
                    const valor = new FormData(event.currentTarget).get("valor")?.toString().trim();
                    resolver(valor || null);
                });
                modalHost.querySelectorAll("[data-modal-cancel], .modal-backdrop").forEach(elemento => {
                    elemento.addEventListener("click", () => resolver(null));
                });
                modalHost.querySelector("textarea")?.focus();
            });
        }

        // Catálogo único de canales/redes soportados. Antes existían DOS
        // copias de este catálogo y de sus helpers (una en inbox.js, otra
        // aquí). Al cargarse ambos scripts, la definición de utils.js
        // pisaba silenciosamente a la de inbox.js -y esa versión no sabía
        // resolver el filtro "TODOS" ni leer conversacion.canalOrigen /
        // PascalCase-, así que el badge "Todas" del inbox perdía su color
        // y ciertas conversaciones caían mal clasificadas. Ahora hay una
        // sola fuente de verdad, usada por inbox.js, leads.js, dashboard.js,
        // connections.js, bot.js y las notificaciones.
        const REDES_DISPONIBLES = [
            { canal: "TODOS", nombre: "Todas", clase: "all" },
            { canal: "WHATSAPP", nombre: "WhatsApp", clase: "whatsapp" },
            { canal: "INSTAGRAM", nombre: "Instagram", clase: "instagram" },
            { canal: "FACEBOOK", nombre: "Facebook", clase: "facebook" },
            { canal: "TIKTOK", nombre: "TikTok", clase: "tiktok" }
        ];

        function normalizarCanal(canal) {
            return String(canal || "WHATSAPP").trim().toUpperCase();
        }

        function obtenerRedPorCanal(canal) {
            const codigo = normalizarCanal(canal);
            return REDES_DISPONIBLES.find(red => red.canal === codigo)
                || REDES_DISPONIBLES.find(red => red.canal === "WHATSAPP");
        }

        function obtenerCanalConversacion(conversacion) {
            return normalizarCanal(
                conversacion?.canal ||
                conversacion?.Canal ||
                conversacion?.canalOrigen ||
                conversacion?.CanalOrigen
            );
        }

        function obtenerIniciales(nombre) {
            return (nombre || "U")
                .split(" ")
                .filter(Boolean)
                .slice(0, 2)
                .map(parte => parte.charAt(0))
                .join("")
                .toUpperCase();
        }

        function estadosConversacion() {
            return [
                { id: "NUEVO", label: "Nuevo" },
                { id: "ABIERTO", label: "Abierto" },
                { id: "EN_ATENCION", label: "En atencion" },
                { id: "ESPERANDO_CLIENTE", label: "Esperando cliente" },
                { id: "COTIZACION_ENVIADA", label: "Cotización enviada" },
                { id: "CERRADO", label: "Cerrado" },
                { id: "PERDIDO", label: "Perdido" },
                { id: "NO_RESPONDIO", label: "No respondio" }
            ];
        }

        function estadosConversacionPorRol() {
            const base = estadosConversacion();
            const rol = normalizarRol(rolActual);

            if (rol === "asesor" || rol === "supervisor") {
                return base;
            }

            return base;
        }
