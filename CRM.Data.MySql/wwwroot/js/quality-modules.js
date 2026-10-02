// Control operativo: fallos de integración.

let fallosPagina = 1;
async function cargarModuloFallos(vista) {
    if (!puedeVerModulo("fallos")) {
        vista.innerHTML = '<div class="error">No tienes permiso para ver fallos.</div>';
        return;
    }

    const response = await api(`/api/crm/fallos?page=${fallosPagina}&pageSize=${obtenerTamanoPagina("fallos")}`);
    if (!response.ok) throw new Error("Fallos no disponibles");
    const pagina = await response.json();
    const fallos = pagina.items || [];

    vista.innerHTML = `
        <div class="module-heading">
            <div>
                <h1>Fallos</h1>
                <p>Errores de envío e integración con el contexto exacto donde ocurrieron.</p>
            </div>
        </div>
        <section class="failure-list">
            ${fallos.map(renderFallo).join("") || '<div class="empty">Sin fallos registrados.</div>'}
        </section>
        ${renderPaginacion(pagina, "fallos")}`;

    enlazarPaginacion(vista, page => { fallosPagina = page; return cargarModuloFallos(vista); });

    vista.querySelectorAll("[data-open-failure-chat]").forEach(button => {
        button.addEventListener("click", () => abrirDetalleConversacion(button.dataset.openFailureChat, "fallos"));
    });
}

function renderFallo(item) {
    const contenido = interpretarContenidoFallo(item.texto);
    const esMensaje = String(item.tipo || "").toLowerCase() === "mensaje";
    const contexto = [
        item.conversacionId ? ["Conversación", `#${item.conversacionId}`] : null,
        item.id ? [esMensaje ? "Mensaje" : "Evento", `#${item.id}`] : null,
        item.direccion ? ["Dirección", item.direccion] : null,
        item.tipoMensaje ? ["Tipo", item.tipoMensaje] : null,
        item.conversacionEstado ? ["Estado del chat", item.conversacionEstado] : null,
        !esMensaje && item.entidadId ? ["Registro relacionado", `#${item.entidadId}`] : null
    ].filter(Boolean);

    return `
        <article class="failure-card">
            <header class="failure-card-head">
                <div>
                    <span class="failure-kind">${escapeHtml(item.tipo || "Error")}</span>
                    <h2>${escapeHtml(item.canal || "Integración")}</h2>
                </div>
                <time>${formatearFecha(item.fecha)}</time>
            </header>
            <div class="failure-error-location">
                <strong>Dónde ocurrió</strong>
                <span>${escapeHtml(describirUbicacionFallo(item))}</span>
            </div>
            ${item.clienteNombre || item.clienteTelefono ? `
                <div class="failure-customer">
                    <span>Cliente</span>
                    <strong>${escapeHtml(item.clienteNombre || "Sin nombre")}</strong>
                    ${item.clienteTelefono ? `<small>${escapeHtml(item.clienteTelefono)}</small>` : ""}
                </div>` : ""}
            <dl class="failure-context">
                ${contexto.map(([label, value]) => `<div><dt>${escapeHtml(label)}</dt><dd>${escapeHtml(String(value))}</dd></div>`).join("")}
            </dl>
            <div class="failure-reason">
                <span>Error registrado</span>
                <strong>${escapeHtml(limpiarDetalleFallo(item.detalle))}</strong>
            </div>
            ${contenido.html}
            ${item.externalId ? `<div class="failure-external-id"><span>ID externo</span><code>${escapeHtml(item.externalId)}</code></div>` : ""}
            ${item.conversacionId && !esRol("auditor") ? `
                <div class="failure-actions">
                    <button type="button" class="mini-action" data-open-failure-chat="${item.conversacionId}">Ir a la conversación</button>
                </div>` : ""}
        </article>`;
}

function describirUbicacionFallo(item) {
    if (item.conversacionId) {
        const partes = [
            `Chat #${item.conversacionId}`,
            item.direccion ? `mensaje ${String(item.direccion).toLowerCase()}` : null,
            item.tipoMensaje ? String(item.tipoMensaje).toLowerCase() : null
        ].filter(Boolean);
        return partes.join(" · ");
    }

    return `${item.tipo || "Integración"}${item.entidadId ? ` #${item.entidadId}` : ""} · ${item.canal || "evento"}`;
}

function limpiarDetalleFallo(detalle) {
    const valor = String(detalle || "Error sin detalle").trim();
    return valor.replace(/^FALLIDO:\s*/i, "").replace(/^ERROR:\s*/i, "");
}

function interpretarContenidoFallo(texto) {
    const valor = String(texto || "").trim();
    if (!valor) return { html: '<div class="failure-message empty">Sin texto o payload asociado.</div>' };

    try {
        const payload = JSON.parse(valor);
        if (payload && typeof payload === "object" && !Array.isArray(payload)) {
            const nombre = payload.nombre || payload.name || payload.fileName;
            const mime = payload.mimeType || payload.tipo || payload.type;
            const url = payload.url;
            return {
                html: `<div class="failure-payload">
                    <span>Archivo o payload implicado</span>
                    ${nombre ? `<strong>${escapeHtml(String(nombre))}</strong>` : ""}
                    ${mime ? `<small>${escapeHtml(String(mime))}</small>` : ""}
                    ${url ? `<code title="${escapeAttribute(String(url))}">${escapeHtml(String(url))}</code>` : ""}
                    ${!nombre && !mime && !url ? `<pre>${escapeHtml(JSON.stringify(payload, null, 2))}</pre>` : ""}
                </div>`
            };
        }
    } catch {
        // Los mensajes de texto normales no son JSON.
    }

    return {
        html: `<div class="failure-message">
            <span>Mensaje implicado</span>
            <div class="failure-chat-bubble">${escapeHtml(valor)}</div>
        </div>`
    };
}

function descargarArchivo(url) {
    const link = document.createElement("a");
    link.href = url;
    document.body.appendChild(link);
    link.click();
    link.remove();
}
