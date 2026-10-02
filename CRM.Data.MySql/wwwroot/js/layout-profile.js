// Módulo frontend del CRM.

        function limpiarIconosDePanelExtra() {
            document.querySelectorAll('.workspace-nav [data-lucide^="panel-left-"]').forEach(icon => {
                if (!navToggle.contains(icon)) icon.remove();
            });
        }

        const mobileMenuButton = document.getElementById("mobileMenuButton");
        const mobileMenuBackdrop = document.getElementById("mobileMenuBackdrop");
        const mobileMenu = document.querySelector(".workspace-nav");
        function cerrarMenuMovil() {
            document.body.classList.remove("mobile-menu-open");
            mobileMenuButton?.setAttribute("aria-expanded", "false");
        }
        mobileMenuButton?.addEventListener("click", () => {
            const abierto = document.body.classList.toggle("mobile-menu-open");
            mobileMenuButton.setAttribute("aria-expanded", String(abierto));
        });
        mobileMenuBackdrop?.addEventListener("click", cerrarMenuMovil);
        mobileMenu?.querySelectorAll("button").forEach(button => {
            button.addEventListener("click", () => {
                if (window.matchMedia("(max-width: 1100px)").matches && !button.classList.contains("nav-group-toggle")) {
                    cerrarMenuMovil();
                }
            });
        });

        function actualizarEstadoNavegacion(colapsada) {
            nav.classList.toggle("collapsed", colapsada);
            navToggle.title = colapsada ? "Expandir menú" : "Contraer menú";
            navToggle.setAttribute("aria-label", navToggle.title);
            navToggle.setAttribute("aria-pressed", colapsada ? "true" : "false");
            limpiarIconosDePanelExtra();
        }

        actualizarEstadoNavegacion(false);
        navToggle.addEventListener("click", () => {
            if (window.matchMedia("(max-width: 1100px)").matches) {
                cerrarMenuMovil();
                return;
            }
            actualizarEstadoNavegacion(!nav.classList.contains("collapsed"));
        });
        navBrandMark.addEventListener("click", () => {
            if (nav.classList.contains("collapsed")) actualizarEstadoNavegacion(false);
        });
        if (window.lucide) window.lucide.createIcons();
        limpiarIconosDePanelExtra();

        document.querySelectorAll("[data-social-brand]").forEach(item => {
            const source = document.querySelector(`.nav-subitem[data-channel="${item.dataset.socialBrand}"] .brand-logo`);
            const slot = item.querySelector(".social-brand-slot");
            if (source && slot) slot.replaceWith(source.cloneNode(true));
        });

        function aplicarEnlacesPublicos(canales = []) {
            const enlaces = new Map(canales.map(canal => [String(canal.canal || "").toUpperCase(), canal.publicUrl || ""]));
            document.querySelectorAll("[data-social-brand]").forEach(item => {
                const urlConfigurada = enlaces.get(item.dataset.socialBrand) || item.getAttribute("href") || "";
                let urlValida = "";
                try {
                    const candidata = new URL(urlConfigurada);
                    if (["http:", "https:"].includes(candidata.protocol)) urlValida = candidata.href;
                } catch { }

                if (urlValida) {
                    item.href = urlValida;
                    item.target = "_blank";
                    item.rel = "noopener noreferrer";
                    item.classList.remove("social-link-pending");
                    item.removeAttribute("aria-disabled");
                } else {
                    item.removeAttribute("href");
                    item.removeAttribute("target");
                    item.classList.add("social-link-pending");
                    item.setAttribute("aria-disabled", "true");
                }
            });
        }

        async function cargarEnlacesPublicos() {
            try {
                const response = await fetch("/api/integraciones/canales", { credentials: "same-origin" });
                if (response.ok) aplicarEnlacesPublicos(await response.json());
            } catch { }
        }

        window.actualizarEnlacesPublicos = aplicarEnlacesPublicos;
        cargarEnlacesPublicos();

        // obtenerIniciales() vive en utils.js (única fuente; antes estaba
        // duplicada aquí y en customer-panel.js con el mismo código).

        function claveFotoPerfil() {
            return `crm.profile.photo.${sesionActual?.usuario || "local"}`;
        }

        function pintarAvatarPerfil(url = null) {
            const iniciales = obtenerIniciales(sesionActual?.usuario);
            document.querySelectorAll("#profileAvatar, #profileAvatarMenu").forEach(avatar => {
                avatar.innerHTML = "";
                avatar.style.backgroundImage = "";
                if (url) {
                    avatar.style.backgroundImage = `url("${url}")`;
                    avatar.classList.add("has-photo");
                } else {
                    avatar.innerHTML = '<svg class="profile-user-icon" viewBox="0 0 32 32" aria-hidden="true"><circle cx="16" cy="10" r="5"></circle><path d="M6 27c.8-5.6 4.3-8.5 10-8.5S25.2 21.4 26 27"></path></svg>';
                    avatar.classList.remove("has-photo");
                }
            });
        }

        function renderPerfilUsuario(sesion) {
            sesionActual = sesion;
            document.getElementById("profileName").textContent = sesion.usuario || "Usuario";
            document.getElementById("profileRole").textContent = sesion.rol || "Sin rol";
            pintarAvatarPerfil(localStorage.getItem(claveFotoPerfil()));
        }

        const themeMenuButton = document.getElementById("themeMenuButton");
        const themeMenuDropdown = document.getElementById("themeMenuDropdown");

        function aplicarTemaCRM(oscuro, guardar = true) {
            document.documentElement.classList.toggle("theme-dark", oscuro);
            if (guardar) {
                try {
                    localStorage.setItem("crm.theme", oscuro ? "dark" : "light");
                } catch {
                    // El tema sigue aplicado aunque el navegador bloquee localStorage.
                }
            }

            const botonTema = document.getElementById("themeToggleButton");
            if (!botonTema) return;
            botonTema.innerHTML = `<i data-lucide="${oscuro ? "sun" : "moon"}"></i><span>${oscuro ? "Modo claro" : "Modo oscuro"}</span>`;
            if (window.lucide) window.lucide.createIcons();
        }

        function aplicarModoTema(modo) {
            const modoNormalizado = ["system", "light", "dark"].includes(modo) ? modo : "system";
            try { localStorage.setItem("crm.theme", modoNormalizado); } catch { }
            const oscuro = modoNormalizado === "dark" || (modoNormalizado === "system" && window.matchMedia("(prefers-color-scheme: dark)").matches);
            aplicarTemaCRM(oscuro, false);
            document.querySelectorAll(".theme-choice").forEach(choice => choice.classList.toggle("active", choice.dataset.themeChoice === modoNormalizado));
            if (themeMenuButton) {
                themeMenuButton.classList.toggle("active", modoNormalizado !== "system");
                themeMenuButton.setAttribute("title", modoNormalizado === "dark" ? "Oscuro" : modoNormalizado === "light" ? "Claro" : "Sistema");
                themeMenuButton.setAttribute("aria-label", `Tema: ${modoNormalizado === "dark" ? "oscuro" : modoNormalizado === "light" ? "claro" : "sistema"}`);
                themeMenuButton.innerHTML = `<i data-lucide="${modoNormalizado === "dark" ? "moon" : modoNormalizado === "light" ? "sun" : "laptop-minimal"}"></i>`;
                if (window.lucide) window.lucide.createIcons();
            }
        }

        const modoGuardado = localStorage.getItem("crm.theme") || "system";
        aplicarModoTema(modoGuardado);

        themeMenuButton?.addEventListener("click", event => {
            event.stopPropagation();
            const abierto = themeMenuDropdown.classList.toggle("hidden");
            themeMenuButton.setAttribute("aria-expanded", String(!abierto));
            themeMenuButton.classList.toggle("open", !abierto);
            if (!abierto && window.lucide) window.lucide.createIcons();
        });
        themeMenuDropdown?.querySelectorAll("[data-theme-choice]").forEach(choice => {
            choice.addEventListener("click", event => {
                event.stopPropagation();
                aplicarModoTema(choice.dataset.themeChoice);
                themeMenuDropdown.classList.add("hidden");
                themeMenuButton?.setAttribute("aria-expanded", "false");
                themeMenuButton?.classList.remove("open");
            });
        });

        function alternarMenuPerfil(abierto = null) {
            const debeAbrir = abierto == null ? profileDropdown.classList.contains("hidden") : abierto;
            profileDropdown.classList.toggle("hidden", !debeAbrir);
            profileButton.setAttribute("aria-expanded", String(debeAbrir));
            if (debeAbrir && window.lucide) window.lucide.createIcons();
        }

        profileButton.addEventListener("click", event => {
            event.stopPropagation();
            alternarMenuPerfil();
        });

        document.addEventListener("click", event => {
            if (!event.target.closest(".profile-menu")) alternarMenuPerfil(false);
            if (!event.target.closest(".theme-menu")) {
                themeMenuDropdown?.classList.add("hidden");
                themeMenuButton?.setAttribute("aria-expanded", "false");
                themeMenuButton?.classList.remove("open");
            }
            if (!event.target.closest(".notification-menu") && typeof alternarCentroNotificaciones === "function") {
                alternarCentroNotificaciones(false);
            }
        });

        notificationButton?.addEventListener("click", event => {
            event.stopPropagation();
            if (typeof alternarCentroNotificaciones === "function") alternarCentroNotificaciones();
        });

        clearNotificationsButton?.addEventListener("click", event => {
            event.stopPropagation();
            if (typeof limpiarCentroNotificaciones === "function") limpiarCentroNotificaciones();
        });

        markNotificationsReadButton?.addEventListener("click", event => {
            event.stopPropagation();
            if (typeof marcarTodasNotificacionesLeidas === "function") marcarTodasNotificacionesLeidas();
        });

        changePhotoButton.addEventListener("click", () => profilePhotoInput.click());
        profilePhotoInput.addEventListener("change", () => {
            const archivo = profilePhotoInput.files && profilePhotoInput.files[0];
            if (!archivo) return;
            const reader = new FileReader();
            reader.onload = () => {
                localStorage.setItem(claveFotoPerfil(), reader.result);
                pintarAvatarPerfil(reader.result);
            };
            reader.readAsDataURL(archivo);
        });

        viewProfileButton.addEventListener("click", () => {
            alternarMenuPerfil(false);
            mostrarModalInfo("Perfil", `
                <div class="modal-profile">
                    <span class="profile-avatar large">${escapeHtml(obtenerIniciales(sesionActual?.usuario))}</span>
                    <div>
                        <strong>${escapeHtml(sesionActual?.usuario || "-")}</strong>
                        <span>${escapeHtml(sesionActual?.rol || "-")}</span>
                    </div>
                </div>`);
        });

        document.getElementById("themeToggleButton")?.addEventListener("click", event => {
            event.stopPropagation();
            aplicarModoTema(document.documentElement.classList.contains("theme-dark") ? "light" : "dark");
        });

        logoutButton.addEventListener("click", async () => {
            logoutButton.disabled = true;
            logoutButton.textContent = "Cerrando...";
            try {
                await fetch("/api/auth/logout", {
                    method: "POST",
                    credentials: "same-origin",
                    headers: { "Cache-Control": "no-cache" }
                });
            } finally {
                window.location.href = "/login.html";
            }
        });
