// Módulo frontend del CRM.

async function actualizarCRM() {
    if (actualizacionEnCurso || envioEnCurso) return;
    if (document.activeElement?.closest("#details")) return;

    const requiereInbox = moduloActual === "inbox" || (moduloActual === "dashboard" && puedeVerModulo("inbox"));
    if (!requiereInbox && !modoDetalleConversacion) {
        estado.textContent = "API conectada";
        return;
    }

    actualizacionEnCurso = true;
    try {
        const id = conversacionSeleccionada ? conversacionSeleccionada.id : null;
        if (requiereInbox) await cargarConversaciones();
        if (id && Number(conversacionSeleccionada?.id) === Number(id))
            await seleccionarConversacion(id, { refrescarFicha: false, refresco: true });
        estado.textContent = "API conectada";
    } catch (error) {
        console.error(error);
        estado.textContent = "Error de conexion";
    } finally {
        actualizacionEnCurso = false;
    }
}

const enviosTextoPendientes = new Map();

async function enviarMensaje() {
    if (!conversacionSeleccionada || envioEnCurso || conversacionSeleccionada.puedeAtender === false) return;

    if (archivosPendientes.length) {
        await enviarArchivo();
        return;
    }

    const texto = input.value.trim();
    if (!texto) return;
    envioEnCurso = true;
    boton.disabled = true;
    input.value = "";
    const conversacionId = conversacionSeleccionada.id;
    const respuestaSeleccionada = mensajeRespuestaSeleccionado;
    const claveEnvio = JSON.stringify([conversacionId, texto, respuestaSeleccionada?.id || null]);
    const requestId = enviosTextoPendientes.get(claveEnvio) || crearIdEnvio();
    enviosTextoPendientes.set(claveEnvio, requestId);
    limpiarRespuestaSeleccionada();
    const temporalId = agregarMensajeOptimista(texto, respuestaSeleccionada);
    try {
        await asegurarConversacionTomada();
        const response = await api(`/api/whatsapp/conversaciones/${conversacionId}/mensajes`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({
                mensaje: texto,
                usuarioId: sesionActual?.id ? Number(sesionActual.id) : null,
                tipo: "text",
                clientRequestId: requestId,
                replyToMessageId: respuestaSeleccionada?.id ? Number(respuestaSeleccionada.id) : null
            })
        });
        if (!response.ok) throw new Error("No se pudo enviar el mensaje");
        enviosTextoPendientes.delete(claveEnvio);
        if (Number(conversacionSeleccionada?.id) === Number(conversacionId))
            await seleccionarConversacion(conversacionId, { refrescarFicha: false, refresco: true })
                .catch(error => console.error("Mensaje enviado; pendiente de refrescar", error));
        cargarConversaciones().catch(error => console.error("No se pudo refrescar la bandeja", error));
    } catch (error) {
        console.error(error);
        if (temporalId && conversacionSeleccionada?.id === conversacionId) {
            conversacionSeleccionada.mensajes = (conversacionSeleccionada.mensajes || []).map(mensaje =>
                mensaje.id === temporalId
                    ? { ...mensaje, estado: "ERROR" }
                    : mensaje);
            renderizarMensajesConversacion(conversacionSeleccionada.mensajes);
        }
        if (Number(conversacionSeleccionada?.id) === Number(conversacionId)) {
            input.value = texto;
            if (respuestaSeleccionada) seleccionarMensajeParaResponder(respuestaSeleccionada);
        }
        notificar("No se pudo enviar el mensaje.", "error");
    } finally {
        envioEnCurso = false;
        boton.disabled = !conversacionSeleccionada || !tienePermiso("mensajes.enviar") || conversacionSeleccionada.puedeAtender === false;
        if (Number(conversacionSeleccionada?.id) === Number(conversacionId)) input.focus();
    }
}

function crearIdEnvio() {
    if (window.crypto?.randomUUID) return window.crypto.randomUUID();
    const bytes = window.crypto.getRandomValues(new Uint8Array(16));
    bytes[6] = (bytes[6] & 15) | 64;
    bytes[8] = (bytes[8] & 63) | 128;
    const hex = [...bytes].map(byte => byte.toString(16).padStart(2, "0")).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

async function asegurarConversacionTomada(conversacion = conversacionSeleccionada) {
    if (!conversacion) return;
    const asignado = conversacion.usuarioAsignadoId || conversacion.usuarioAsignado?.id || conversacion.usuarioAsignado;
    if (asignado) return;
    const response = await api(`/api/crm/conversaciones/${conversacion.id}/tomar`, { method: "PUT" });
    if (!response.ok) throw new Error("No se pudo asignar la conversación antes de responder.");
}

function actualizarPreviewArchivo() {
    [...fileInput.files].forEach(archivo => {
        const id = window.crypto?.randomUUID?.() ||
            `${Date.now()}-${Math.random().toString(16).slice(2)}`;
        archivosPendientes.push({
            id,
            archivo,
            url: URL.createObjectURL(archivo),
            estado: "listo"
        });
    });
    fileInput.value = "";
    renderizarArchivosPendientes();
}

function renderizarArchivosPendientes() {
    attachmentList.innerHTML = archivosPendientes.map(item => {
        const { archivo } = item;
        const esImagen = archivo.type.startsWith("image/") || /\.(jpe?g|png|webp)$/i.test(archivo.name);
        const estado = item.estado === "subiendo"
            ? "Subiendo..."
            : item.estado === "error"
                ? "No se pudo enviar"
                : formatearTamanoArchivo(archivo.size);
        return `<div class="attachment-item ${item.estado === "error" ? "has-error" : ""}">
                    <div class="attachment-thumb">${esImagen
                        ? `<img src="${escapeAttribute(item.url)}" alt="">`
                        : '<i data-lucide="file"></i>'}</div>
                    <div class="attachment-meta">
                        <div class="attachment-name" title="${escapeAttribute(archivo.name)}">${escapeHtml(archivo.name)}</div>
                        <div class="attachment-size">${estado}</div>
                    </div>
                    <button class="attachment-remove" type="button" data-remove-attachment="${escapeAttribute(item.id)}"
                        aria-label="Quitar ${escapeAttribute(archivo.name)}" ${item.estado === "subiendo" ? "disabled" : ""}>
                        <i data-lucide="x"></i>
                    </button>
                </div>`;
    }).join("");
    attachmentPreview.classList.toggle("visible", archivosPendientes.length > 0);
    fileName.textContent = archivosPendientes.length
        ? `${archivosPendientes.length} ${archivosPendientes.length === 1 ? "archivo" : "archivos"} seleccionado${archivosPendientes.length === 1 ? "" : "s"}`
        : "";
    if (window.lucide) window.lucide.createIcons();
}

function formatearTamanoArchivo(bytes) {
    return bytes > 1024 * 1024
        ? `${(bytes / (1024 * 1024)).toFixed(1)} MB`
        : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

function quitarArchivoPendiente(id) {
    const indice = archivosPendientes.findIndex(item => item.id === id);
    if (indice < 0 || archivosPendientes[indice].estado === "subiendo") return;
    URL.revokeObjectURL(archivosPendientes[indice].url);
    archivosPendientes.splice(indice, 1);
    renderizarArchivosPendientes();
}

async function enviarArchivo() {
    if (envioEnCurso) return;

    if (!conversacionSeleccionada || conversacionSeleccionada.puedeAtender === false) {
        notificar("Selecciona una conversacion antes de enviar un archivo.", "error");
        return;
    }

    if (!archivosPendientes.length) {
        notificar("No has seleccionado ningun archivo.", "error");
        return;
    }

    const conversacionId = conversacionSeleccionada.id;
    const pendientes = [...archivosPendientes];
    envioEnCurso = true;
    boton.disabled = true;
    attachButton.disabled = true;
    try {
        await asegurarConversacionTomada();
        let refrescarConversacion = false;

        for (const item of pendientes) {
            if (!archivosPendientes.some(actual => actual.id === item.id)) continue;

            item.estado = "subiendo";
            renderizarArchivosPendientes();
            const { archivo } = item;
            const datos = new FormData();
            datos.append("archivo", archivo);
            datos.append("usuarioId", sesionActual?.id ? String(sesionActual.id) : "");
            datos.append("clientRequestId", item.id);

            try {
                const response = await api(`/api/whatsapp/conversaciones/${conversacionId}/archivos`, {
                    method: "POST",
                    body: datos
                });
                if (!response.ok) {
                    const cuerpo = await response.text();
                    let detalle = {};
                    try {
                        detalle = JSON.parse(cuerpo);
                    } catch {
                        detalle.message = cuerpo;
                    }
                    if (response.status === 409 && detalle.duplicate) {
                        quitarArchivoProcesado(item);
                        refrescarConversacion = true;
                        continue;
                    }
                    throw new Error(detalle.message || `Error HTTP ${response.status}`);
                }

                const resultado = await response.json();
                quitarArchivoProcesado(item);
                if (conversacionSeleccionada?.id === conversacionId) {
                    agregarArchivoConfirmado(archivo, resultado);
                }
                refrescarConversacion = true;
            } catch (error) {
                console.error(error);
                item.estado = "error";
                renderizarArchivosPendientes();
                notificar(`No se pudo enviar ${archivo.name}: ${error.message || "error de envio"}`, "error");
            }
        }

        if (refrescarConversacion && conversacionSeleccionada?.id === conversacionId) {
            seleccionarConversacion(conversacionId, { refrescarFicha: false, refresco: true })
                .catch(error => console.error("No se pudo actualizar la conversacion", error));
        }
        cargarConversaciones().catch(error => console.error("No se pudo refrescar la bandeja", error));
    } catch (error) {
        console.error(error);
        notificar(error.message || "No se pudo enviar el archivo.", "error");
    } finally {
        envioEnCurso = false;
        attachButton.disabled = !conversacionSeleccionada || !tienePermiso("mensajes.enviar") || conversacionSeleccionada.puedeAtender === false;
        boton.disabled = !conversacionSeleccionada || !tienePermiso("mensajes.enviar") || conversacionSeleccionada.puedeAtender === false;
        renderizarArchivosPendientes();
    }
}

function quitarArchivoProcesado(item) {
    const indice = archivosPendientes.findIndex(actual => actual.id === item.id);
    if (indice < 0) return;
    URL.revokeObjectURL(archivosPendientes[indice].url);
    archivosPendientes.splice(indice, 1);
    renderizarArchivosPendientes();
}

function agregarArchivoConfirmado(archivo, resultado) {
    const tipo = archivo.type.startsWith("image/") || /\.(jpe?g|png|webp)$/i.test(archivo.name)
        ? "image"
        : "document";
    const mensajeId = Number(resultado.mensajeId);
    const mensajesActuales = conversacionSeleccionada.mensajes || [];
    if (mensajesActuales.some(mensaje => Number(mensaje.id) === mensajeId)) return;

    const mensaje = {
        id: mensajeId,
        direccion: "S",
        tipo,
        estado: "ENVIANDO",
        mensaje: JSON.stringify({
            nombre: archivo.name,
            url: resultado.url,
            mimeType: archivo.type || "application/octet-stream",
            tamano: archivo.size
        }),
        fecha: new Date().toISOString()
    };
    conversacionSeleccionada.mensajes = [...mensajesActuales, mensaje];
    conversacionSeleccionada.ultimoMensaje = mensaje.fecha;
    renderizarMensajesConversacion(conversacionSeleccionada.mensajes);
}
