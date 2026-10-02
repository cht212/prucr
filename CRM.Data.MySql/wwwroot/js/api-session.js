// Módulo frontend del CRM.

        async function api(url, options = {}) {
            const separador = url.includes("?") ? "&" : "?";
            const requestOptions = {
                cache: "no-store",
                credentials: "same-origin",
                ...options,
                headers: {
                    "Cache-Control": "no-cache",
                    ...(options.headers || {})
                }
            };

            // Cada navegación crea un AbortController. Así una consulta lenta
            // del módulo anterior no puede terminar pintando encima del módulo actual.
            if (["GET", "HEAD"].includes(String(requestOptions.method || "GET").toUpperCase()) &&
                !requestOptions.signal && window.__crmNavigationController?.signal) {
                requestOptions.signal = window.__crmNavigationController.signal;
            }

            const response = await fetch(`${url}${separador}_=${Date.now()}`, requestOptions);
            if (response.status === 401) {
                window.location.href = "/login.html";
            }
            return response;
        }

        async function refrescarSesionActual() {
            const response = await api("/api/auth/me");
            if (!response.ok) {
                throw new Error("No se pudieron actualizar los permisos de la sesión.");
            }

            const anterior = sesionActual;
            sesionActual = await response.json();
            rolActual = sesionActual.rol || "";
            if (JSON.stringify(anterior?.permisos) !== JSON.stringify(sesionActual.permisos) || anterior?.rol !== rolActual) {
                usuariosCache = null;
                asesorFiltroActivo = tienePermiso("comunicaciones.chats.todos") ? "" : String(sesionActual.id || "");
            }
            aplicarNavegacionPorRol();
            return sesionActual;
        }

        function ocultarTodoElMenu() {
            document.querySelectorAll(".nav-item[data-module]").forEach(item => {
                item.classList.add("hidden");
                item.hidden = true;
            });
            document.getElementById("usersNav") && (document.getElementById("usersNav").hidden = true);
            document.getElementById("activityNav") && (document.getElementById("activityNav").hidden = true);
            document.getElementById("failuresNav") && (document.getElementById("failuresNav").hidden = true);
            document.getElementById("marketingNav") && (document.getElementById("marketingNav").hidden = true);
        }

        async function iniciarAplicacion() {
            ocultarTodoElMenu();

            try {
                const response = await fetch("/api/auth/me", {
                    cache: "no-store",
                    credentials: "same-origin"
                });

                if (!response.ok) {
                    window.location.href = "/login.html";
                    return;
                }

                const sesion = await response.json();
                renderPerfilUsuario({ ...sesion, rol: sesion.rolNombre || sesion.rol });
                cargarNotificacionesPersistidas();
                renderFiltrosRedBandeja();

                rolActual = sesion.rol || "";
                sesionActual = sesion;

                if (normalizarRol(rolActual) === "asesor" && sesion.id && !tienePermiso("comunicaciones.chats.todos")) {
                    asesorFiltroActivo = String(sesion.id);
                } else {
                    asesorFiltroActivo = "";
                }

                if (estado) {
                    estado.textContent = `${sesion.usuario} · ${sesion.rolNombre || sesion.rol}`;
                }

                aplicarNavegacionPorRol();

                // Espera a que el primer módulo se monte. Antes se lanzaba sin
                // await y el polling podía competir con el primer render.
                await abrirModulo(moduloInicialPorRol());

                if (typeof iniciarActualizacionesTiempoReal === "function") {
                    iniciarActualizacionesTiempoReal();
                }

                if (typeof actualizarCRM === "function") {
                    setInterval(() => {
                        if (document.visibilityState === "visible" &&
                            moduloActual === "inbox" &&
                            typeof actualizarCRM === "function") {
                            Promise.resolve(actualizarCRM()).catch(error =>
                                console.warn("No se pudo actualizar el CRM:", error)
                            );
                        }
                    }, POLLING_MS);
                }
            } catch (error) {
                console.error("No se pudo iniciar el CRM:", error);
                const vista = document.getElementById("moduleView");
                if (vista) {
                    vista.classList.remove("hidden");
                    vista.innerHTML = `
                        <div class="error">
                            <strong>No se pudo iniciar el CRM.</strong>
                            <p>${escapeHtml(error?.message || "Error inesperado al cargar la aplicación.")}</p>
                        </div>`;
                }
            }
        }

        function normalizarRol(rol) {
            return String(rol || "").trim().toLowerCase();
        }

        function esRol(...roles) {
            const rol = normalizarRol(rolActual);
            return roles.some(item => normalizarRol(item) === rol);
        }

        function tienePermiso(codigo) {
            return Array.isArray(sesionActual?.permisos) &&
                sesionActual.permisos.some(item => String(item).toLowerCase() === String(codigo).toLowerCase());
        }

        function permisoCanalComunicaciones(canal) {
            const permisos = {
                WHATSAPP: "comunicaciones.canal.whatsapp",
                INSTAGRAM: "comunicaciones.canal.instagram",
                FACEBOOK: "comunicaciones.canal.facebook",
                TIKTOK: "comunicaciones.canal.tiktok"
            };
            return permisos[String(canal || "").toUpperCase()] || null;
        }

        function puedeVerCanalComunicaciones(canal) {
            const permiso = permisoCanalComunicaciones(canal);
            return !permiso || tienePermiso(permiso);
        }

        function puedeGestionarEquipoCRM() {
            return esRol("administrador", "supervisor");
        }

        const MODULOS_POR_ROL = {
            administrador: new Set([
                "dashboard",
                "inbox",
                "contactos",
                "tareas",
                "leads",
                "ventas",
                "reportes",
                "marketing",
                "bot",
                "conexiones",
                "actividad",
                "usuarios",
                "fallos"
            ]),
            supervisor: new Set([
                "dashboard",
                "inbox",
                "contactos",
                "tareas",
                "leads",
                "ventas",
                "reportes",
                "marketing",
                "bot",
                "actividad"
            ]),
            auditor: new Set([
                "dashboard",
                "inbox",
                "contactos",
                "tareas",
                "leads",
                "ventas",
                "reportes",
                "marketing",
                "bot",
                "conexiones",
                "actividad",
                "usuarios",
                "fallos"
            ]),
            asesor: new Set([
                "dashboard",
                "inbox",
                "contactos",
                "tareas",
                "leads",
                "ventas"
            ]),
            marketing: new Set([
                "marketing"
            ])
        };

        function modulosPermitidosPorRol() {
            // La API entrega todos los accesos efectivos, incluidas las
            // excepciones al rol. No reintroducir aquí accesos desmarcados.
            if (Array.isArray(sesionActual?.permisos)) {
                return new Set(sesionActual.permisos
                    .filter(item => String(item).startsWith("modulo."))
                    .map(item => String(item).slice("modulo.".length)));
            }
            const rol = normalizarRol(rolActual);

            let modulos;

            if (rol === "administrador") {
                modulos = MODULOS_POR_ROL.administrador;
            } else if (rol === "supervisor") {
                modulos = MODULOS_POR_ROL.supervisor;
            } else if (rol === "asesor") {
                modulos = MODULOS_POR_ROL.asesor;
            } else if (rol === "auditor") {
                modulos = MODULOS_POR_ROL.auditor;
            } else if (rol === "marketing") {
                modulos = MODULOS_POR_ROL.marketing;
            } else {
                modulos = new Set(["inbox"]);
            }

            const result = new Set(modulos);
            (sesionActual?.permisos || [])
                .filter(item => String(item).startsWith("modulo."))
                .forEach(item => result.add(String(item).slice("modulo.".length)));
            return result;
        }

        function moduloInicialPorRol() {
            const permitidos = modulosPermitidosPorRol();
            const preferido = esRol("marketing") ? "marketing" : esRol("auditor") ? "fallos" : "dashboard";
            return permitidos.has(preferido) ? preferido : permitidos.values().next().value || null;
        }

        function puedeVerModulo(modulo) {
            return modulosPermitidosPorRol().has(modulo);
        }

        function aplicarNavegacionPorRol() {
            const permitidos = modulosPermitidosPorRol();
            document.querySelector('[data-nav-group="comunicaciones"]')?.classList.toggle("hidden", !permitidos.has("inbox"));
            document.querySelectorAll('.nav-subitem[data-channel]').forEach(item => {
                const visible = item.dataset.channel === "TODOS" || puedeVerCanalComunicaciones(item.dataset.channel);
                item.classList.toggle("hidden", !visible);
                item.hidden = !visible;
            });
            if (inboxCanalActivo !== "TODOS" && !puedeVerCanalComunicaciones(inboxCanalActivo)) {
                inboxCanalActivo = "TODOS";
            }
            document.querySelectorAll(".nav-item[data-module]").forEach(item => {
                const permitido = permitidos.has(item.dataset.module);
                item.classList.toggle("hidden", !permitido);
                item.hidden = !permitido;
            });

            const dashboardLabel = document.querySelector('.nav-item[data-module="dashboard"] .nav-label');
            if (dashboardLabel) {
                dashboardLabel.textContent = "Inicio";
            }

            document.getElementById("usersNav")?.classList.toggle("hidden", !permitidos.has("usuarios"));
            document.getElementById("usersNav") && (document.getElementById("usersNav").hidden = !permitidos.has("usuarios"));
            document.getElementById("botNav")?.classList.toggle("hidden", !permitidos.has("bot"));
            document.getElementById("botNav") && (document.getElementById("botNav").hidden = !permitidos.has("bot"));
            document.getElementById("connectionsNav")?.classList.toggle("hidden", !permitidos.has("conexiones"));
            document.getElementById("connectionsNav") && (document.getElementById("connectionsNav").hidden = !permitidos.has("conexiones"));
            document.getElementById("activityNav")?.classList.toggle("hidden", !permitidos.has("actividad"));
            document.getElementById("activityNav") && (document.getElementById("activityNav").hidden = !permitidos.has("actividad"));
            document.getElementById("failuresNav")?.classList.toggle("hidden", !permitidos.has("fallos"));
            document.getElementById("failuresNav") && (document.getElementById("failuresNav").hidden = !permitidos.has("fallos"));
            document.getElementById("marketingNav")?.classList.toggle("hidden", !permitidos.has("marketing"));
            document.getElementById("marketingNav") && (document.getElementById("marketingNav").hidden = !permitidos.has("marketing"));
        }
