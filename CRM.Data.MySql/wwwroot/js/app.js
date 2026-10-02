// Módulo frontend del CRM.

        //
        // =========================================================

        const INTERVALO_ESCRIBIENDO_MS = 18000;
        let ultimoAvisoEscribiendo = 0;

        function notificarEscribiendo() {
            if (!conversacionSeleccionada) return;
            const ahora = Date.now();
            if (ahora - ultimoAvisoEscribiendo < INTERVALO_ESCRIBIENDO_MS) return;
            ultimoAvisoEscribiendo = ahora;

            api(`/api/whatsapp/conversaciones/${conversacionSeleccionada.id}/escribiendo`, {
                method: "POST"
            }).catch(error => console.error("No se pudo enviar el indicador de escribiendo", error));
        }

        let inboxSearchTimer = null;
        document.getElementById("searchInput").addEventListener("input", () => {
            clearTimeout(inboxSearchTimer);
            inboxSearchTimer = setTimeout(recargarBandeja, 250);
        });
        document.querySelectorAll(".nav-item").forEach(item => {
            item.addEventListener("click", () => {
                if (item.dataset.module === "inbox") {
                    comunicacionesMenuAbierto = !comunicacionesMenuAbierto;
                    if (!inboxCanalActivo) inboxCanalActivo = "TODOS";
                    renderFiltrosRedBandeja();
                    sincronizarSubmenuComunicaciones();
                    abrirModulo("inbox");
                    return;
                }
                comunicacionesMenuAbierto = false;
                sincronizarSubmenuComunicaciones();
                abrirModulo(item.dataset.module);
            });
        });
        document.querySelectorAll(".nav-subitem").forEach(item => {
            item.addEventListener("click", event => {
                event.preventDefault();
                event.stopPropagation();
                cambiarCanalBandeja(item.dataset.channel || "TODOS");
            });
        });
        document.querySelectorAll(".filter-button").forEach(button => {
            button.addEventListener("click", () => {
                filtroActivo = button.dataset.filter;
                document.querySelectorAll(".filter-button").forEach(item => item.classList.remove("active"));
                button.classList.add("active");
                recargarBandeja();
            });
        });
        document.getElementById("leadBackButton").addEventListener("click", () => abrirModulo(moduloRetornoDetalle));
        document.getElementById("sendButton").addEventListener("click", enviarMensaje);
        attachButton.addEventListener("click", () => fileInput.click());
        attachmentList.addEventListener("click", event => {
            const removeButton = event.target.closest("[data-remove-attachment]");
            if (removeButton) quitarArchivoPendiente(removeButton.dataset.removeAttachment);
        });
        fileInput.addEventListener("change", () => {
            actualizarPreviewArchivo();
        });
        document.getElementById("messageInput").addEventListener("keydown", event => {
            if (event.key === "Enter") {
                event.preventDefault();
                enviarMensaje();
            }
        });
        document.getElementById("messageInput").addEventListener("input", () => {
            notificarEscribiendo();
        });

        const vistaModulosResponsive = document.getElementById("moduleView");
        if (vistaModulosResponsive) {
            prepararTablasResponsivas(vistaModulosResponsive);
            new MutationObserver(cambios => {
                cambios.forEach(cambio => cambio.addedNodes.forEach(nodo => {
                    if (nodo.nodeType === Node.ELEMENT_NODE) prepararTablasResponsivas(nodo);
                }));
            }).observe(vistaModulosResponsive, { childList: true, subtree: true });
        }

        iniciarAplicacion();
