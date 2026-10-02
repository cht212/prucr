// Actualizaciones en tiempo real mediante Server-Sent Events (SSE).

let crmRealtimeSource = null;
let crmRealtimeTimer = null;
let crmRealtimePending = false;
let crmRealtimeLastRefresh = 0;
let crmRealtimeFallbackTimer = null;

function hayEditorCrmActivo() {
    if (document.querySelector("#modalHost [role='dialog']")) return true;
    const vista = document.getElementById("moduleView");
    return document.activeElement?.matches("input, textarea, select, [contenteditable='true']") &&
        (vista?.contains(document.activeElement) || document.getElementById("details")?.contains(document.activeElement));
}

function programarActualizacionTiempoReal() {
    if (document.visibilityState !== "visible") {
        crmRealtimePending = true;
        return;
    }

    const vista = document.getElementById("moduleView");
    if (hayEditorCrmActivo()) {
        crmRealtimePending = true;
        return;
    }

    const esperaMinima = moduloActual === "marketing" ? 2500 : 1200;
    const espera = Math.max(700, esperaMinima - (Date.now() - crmRealtimeLastRefresh));
    clearTimeout(crmRealtimeTimer);
    crmRealtimeTimer = setTimeout(async () => {
        if (hayEditorCrmActivo()) {
            crmRealtimePending = true;
            return;
        }
        crmRealtimePending = false;
        crmRealtimeLastRefresh = Date.now();

        try {
            if ((moduloActual === "inbox" || modoDetalleConversacion) && typeof actualizarCRM === "function") {
                await actualizarCRM();
                return;
            }

            const cargadores = {
                dashboard: typeof cargarModuloDashboard === "function" ? cargarModuloDashboard : null,
                contactos: typeof cargarModuloContactos === "function" ? cargarModuloContactos : null,
                tareas: typeof cargarModuloTareas === "function" ? cargarModuloTareas : null,
                leads: typeof cargarModuloLeads === "function" ? cargarModuloLeads : null,
                ventas: typeof cargarModuloVentas === "function" ? cargarModuloVentas : null,
                reportes: typeof cargarModuloReportes === "function" ? cargarModuloReportes : null,
                marketing: typeof cargarModuloMarketing === "function"
                    ? currentView => cargarModuloMarketing(currentView, { silenciosa: true })
                    : null,
                bot: typeof cargarModuloBot === "function" ? cargarModuloBot : null,
                conexiones: typeof cargarModuloConexiones === "function" ? cargarModuloConexiones : null,
                actividad: typeof cargarModuloActividad === "function" ? cargarModuloActividad : null,
                fallos: typeof cargarModuloFallos === "function" ? cargarModuloFallos : null,
                usuarios: typeof cargarModuloUsuarios === "function" ? cargarModuloUsuarios : null
            };
            const cargar = cargadores[moduloActual];
            if (vista && cargar) await cargar(vista);
        } catch (error) {
            if (error?.name !== "AbortError") {
                console.warn("No se pudo aplicar la actualización en tiempo real.", error);
            }
        }
    }, espera);
}

function iniciarActualizacionesTiempoReal() {
    if (!window.EventSource || crmRealtimeSource) return;

    crmRealtimeSource = new EventSource("/api/realtime/events", { withCredentials: true });
    crmRealtimeSource.addEventListener("changed", programarActualizacionTiempoReal);
    crmRealtimeSource.onerror = () => {
        // EventSource reintenta automáticamente; el polling queda como respaldo.
        estado && (estado.textContent = "Reconectando actualizaciones...");
    };
    crmRealtimeSource.addEventListener("connected", () => {
        estado && (estado.textContent = "API conectada · tiempo real");
    });

    // Respaldo de baja frecuencia: mantiene Dashboard y Marketing al día si
    // un proxy interrumpe SSE o si una red social cambia sin enviar webhook.
    crmRealtimeFallbackTimer ??= setInterval(() => {
        if (document.visibilityState === "visible" && ["dashboard", "marketing"].includes(moduloActual)) {
            programarActualizacionTiempoReal();
        }
    }, 60000);
}

document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible" && crmRealtimePending) {
        programarActualizacionTiempoReal();
    }
});

document.addEventListener("focusout", () => {
    if (crmRealtimePending && document.visibilityState === "visible") {
        programarActualizacionTiempoReal();
    }
});

window.addEventListener("beforeunload", () => {
    crmRealtimeSource?.close();
    if (crmRealtimeFallbackTimer) clearInterval(crmRealtimeFallbackTimer);
});
