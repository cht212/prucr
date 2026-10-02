// Módulo frontend del CRM.

        let moduloNavegacionVersion = 0;

        async function abrirModulo(modulo) {
            if (typeof puedeVerModulo === "function" && !puedeVerModulo(modulo)) {
                modulo = typeof moduloInicialPorRol === "function" ? moduloInicialPorRol() : "inbox";
            }

            if (!modulo) {
                moduloActual = null;
                window.__crmNavigationController?.abort();
                document.querySelectorAll(".sidebar, .chat, .details").forEach(item => item.classList.add("hidden"));
                const sinAcceso = document.getElementById("moduleView");
                sinAcceso.classList.remove("hidden");
                sinAcceso.innerHTML = '<div class="empty">No tienes apartados habilitados. Contacta al administrador para solicitar acceso.</div>';
                return;
            }

            const version = ++moduloNavegacionVersion;

            // Cancela peticiones pendientes del módulo anterior para impedir
            // que una respuesta tardía reemplace el contenido de la navegación actual.
            window.__crmNavigationController?.abort();
            window.__crmNavigationController = new AbortController();

            moduloActual = modulo;
            document.querySelectorAll(".nav-item").forEach(item => {
                item.classList.toggle("active", item.dataset.module === modulo);
            });
            document.querySelector('[data-nav-group="comunicaciones"]')?.classList.toggle("active", modulo === "inbox");
            sincronizarSubmenuComunicaciones();

            modoDetalleConversacion = false;
            document.getElementById("leadDetailBar")?.classList.add("hidden");

            // La ficha móvil usa clases globales en <body> con display
            // prioritario. Si quedan activas al navegar, pueden imponerse al
            // estado hidden del nuevo módulo y mantener la ficha superpuesta.
            document.body.classList.remove("mobile-contact-details-open", "mobile-chat-open");

            const vista = document.getElementById("moduleView");
            const sidebar = document.querySelector(".sidebar");
            const chat = document.querySelector(".chat");
            const details = document.getElementById("details");

            if (!vista || !sidebar || !chat || !details) {
                throw new Error("La estructura principal del CRM está incompleta.");
            }

            vista.dataset.module = modulo;

            if (modulo === "inbox") {
                const puedeVerFicha = tienePermiso("comunicaciones.ficha") || tienePermiso("comunicaciones.ficha.contacto");
                vista.classList.add("hidden");
                sidebar.classList.remove("hidden");
                chat.classList.remove("hidden");
                details.classList.toggle("hidden", !puedeVerFicha);
                document.querySelector(".main")?.classList.toggle("customer-details-collapsed", !puedeVerFicha || fichaClienteColapsada);
                try {
                    await cargarConversaciones();
                } catch (error) {
                    console.error("No se pudieron cargar las conversaciones al abrir Comunicaciones.", error);
                    notificar("No se pudieron cargar las conversaciones. Intenta actualizar la bandeja.", "error");
                }
                return;
            }

            sidebar.classList.add("hidden");
            chat.classList.add("hidden");
            details.classList.add("hidden");
            vista.classList.remove("hidden");
            vista.innerHTML = '<div class="empty">Cargando módulo...</div>';

            try {
                switch (modulo) {
                    case "dashboard":
                        await cargarModuloDashboard(vista);
                        break;
                    case "contactos":
                        await cargarModuloContactos(vista);
                        break;
                    case "tareas":
                        await cargarModuloTareas(vista);
                        break;
                    case "leads":
                        await cargarModuloLeads(vista);
                        break;
                    case "ventas":
                        await cargarModuloVentas(vista);
                        break;
                    case "reportes":
                        await cargarModuloReportes(vista);
                        break;
                    case "marketing":
                        await cargarModuloMarketing(vista);
                        break;
                    case "bot":
                        await cargarModuloBot(vista);
                        break;
                    case "conexiones":
                        await cargarModuloConexiones(vista);
                        break;
                    case "actividad":
                        await cargarModuloActividad(vista);
                        break;
                    case "fallos":
                        await cargarModuloFallos(vista);
                        break;
                    case "usuarios":
                        await cargarModuloUsuarios(vista);
                        break;
                    default:
                        throw new Error(`Módulo no implementado: ${modulo}`);
                }

                if (version !== moduloNavegacionVersion || moduloActual !== modulo) {
                    return;
                }

                if (typeof prepararTablasResponsivas === "function") {
                    prepararTablasResponsivas(vista);
                }
            } catch (error) {
                if (error?.name === "AbortError") {
                    return;
                }

                console.error(`No se pudo cargar el módulo ${modulo}:`, error);

                if (version !== moduloNavegacionVersion || moduloActual !== modulo) {
                    return;
                }

                vista.classList.remove("hidden");
                vista.innerHTML = `
                    <div class="error">
                        <strong>No se pudo cargar el módulo.</strong>
                        <p>${escapeHtml(error.message || "")}</p>
                    </div>`;
            }
        }
