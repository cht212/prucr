// Módulo de configuración del bot y respuestas rápidas.

        async function cargarModuloBot(vista) {
            const puedeConfigurar = tienePermiso("bot.gestionar");
            let bot = {
                enabled: true,
                message: "Hola, gracias por escribirnos. En breve te atenderá un asesor.\n\nResponde con una opción:\n1. Hablar con un asesor\n2. Información de productos\n3. Cotización\n4. Horarios y ubicación",
                maxAutoReplies: 2,
                options: []
            };
            let plantillasRapidas = [];
            let estadoIntegraciones = { canales: [] };
            let aviso = "";

            try {
                const [botResponse, templatesResponse, integrationsResponse] = await Promise.all([
                    api("/api/bot/whatsapp"),
                    api("/api/plantillas-rapidas/admin"),
                    api("/api/integraciones/estado")
                ]);
                if (!botResponse.ok) throw new Error("Bot no disponible");
                bot = await botResponse.json();
                if (templatesResponse.ok) {
                    const templatesData = await templatesResponse.json();
                    plantillasRapidas = Array.isArray(templatesData.templates) ? templatesData.templates : [];
                }
                if (integrationsResponse.ok) {
                    estadoIntegraciones = await integrationsResponse.json();
                }
            } catch (error) {
                console.error(error);
                aviso = '<div class="bot-warning">No se pudo leer la configuración actual del bot. Se muestran valores base.</div>';
            }

            const integracionConectada = canal => {
                if (canal === "whatsapp") {
                    const estado = estadoIntegraciones.whatsapp || {};
                    return Boolean(estado.verifyToken && estado.accessToken && estado.phoneNumberId && estado.businessAccountId);
                }
                const encontrado = (estadoIntegraciones.canales || []).find(item =>
                    String(item.canal || "").toLowerCase() === canal);
                return Boolean(encontrado?.connected);
            };
            const crearCanalMensajeria = (id, nombre, descripcion) => {
                const conectado = integracionConectada(id);
                return {
                    id,
                    clase: id,
                    nombre,
                    estado: !conectado ? "Sin conexión" : bot.enabled ? "Activo" : "Bot apagado",
                    descripcion,
                    listo: conectado,
                    soportado: true
                };
            };
            const canalesBot = [
                crearCanalMensajeria(
                    "whatsapp",
                    "WhatsApp",
                    "Recibe mensajes mediante el webhook de Meta y responde con la configuración global del bot."),
                crearCanalMensajeria(
                    "facebook",
                    "Facebook",
                    "Messenger recibe mensajes y recupera conversaciones recientes si hubo una interrupción."),
                crearCanalMensajeria(
                    "instagram",
                    "Instagram",
                    "Instagram recibe mensajes y recupera conversaciones recientes si hubo una interrupción."),
                {
                    id: "tiktok",
                    clase: "tiktok",
                    nombre: "TikTok",
                    estado: "Sin mensajería directa",
                    descripcion: "La conexión actual de TikTok permite publicaciones e indicadores, pero todavía no mensajes directos.",
                    listo: false,
                    soportado: false
                }
            ];
            if (!canalesBot.some(canal => canal.id === botCanalActivo && canal.soportado)) {
                botCanalActivo = canalesBot.find(canal => canal.soportado)?.id || "whatsapp";
            }
            const opcionesBot = bot.options?.length ? bot.options : [
                { key: "1", title: "Hablar con un asesor", response: "Listo, ya derivamos tu conversación con un asesor. En breve te atenderán.", derivesToAdvisor: true }
            ];
            const canalActivo = canalesBot.find(canal => canal.id === botCanalActivo) || canalesBot[0];
            const crearFilaPlantillaBot = option => `
                <article class="bot-template-row" data-bot-template-row>
                    <label>
                        <span>Opción</span>
                        <input type="text" data-template-key value="${escapeAttribute(option.key || "")}" maxlength="12" placeholder="1" ${puedeConfigurar ? "" : "disabled"}>
                    </label>
                    <label>
                        <span>Título</span>
                        <input type="text" data-template-title value="${escapeAttribute(option.title || "")}" maxlength="80" placeholder="Cotización" ${puedeConfigurar ? "" : "disabled"}>
                    </label>
                    <label class="bot-template-response">
                        <span>Respuesta que envía el bot</span>
                        <textarea data-template-response rows="3" maxlength="400" placeholder="Mensaje para el cliente" ${puedeConfigurar ? "" : "disabled"}>${escapeHtml(option.response || "")}</textarea>
                    </label>
                    <label class="bot-template-check">
                        <input type="checkbox" data-template-derives ${option.derivesToAdvisor === false ? "" : "checked"} ${puedeConfigurar ? "" : "disabled"}>
                        <span>Deriva a asesor</span>
                    </label>
                    ${puedeConfigurar ? `<button type="button" class="bot-template-remove" data-remove-template title="Quitar plantilla" aria-label="Quitar plantilla">
                        <i data-lucide="trash-2"></i>
                    </button>` : ""}
                </article>`;
            const crearFilaPlantillaRapida = template => `
                <article class="bot-template-row" data-quick-template-row>
                    <label>
                        <span>Título</span>
                        <input type="text" data-quick-title value="${escapeAttribute(template.title || "")}" maxlength="80" placeholder="Saludo" ${puedeConfigurar ? "" : "disabled"}>
                    </label>
                    <label>
                        <span>Categoría</span>
                        <input type="text" data-quick-category value="${escapeAttribute(template.category || "General")}" maxlength="40" placeholder="Ventas" ${puedeConfigurar ? "" : "disabled"}>
                    </label>
                    <label class="bot-template-response">
                        <span>Mensaje para insertar</span>
                        <textarea data-quick-message rows="3" maxlength="1000" placeholder="Texto que usará el asesor" ${puedeConfigurar ? "" : "disabled"}>${escapeHtml(template.message || "")}</textarea>
                    </label>
                    <label class="bot-template-check">
                        <input type="checkbox" data-quick-enabled ${template.enabled === false ? "" : "checked"} ${puedeConfigurar ? "" : "disabled"}>
                        <span>Activa</span>
                    </label>
                    ${puedeConfigurar ? `<button type="button" class="bot-template-remove" data-remove-quick-template title="Quitar plantilla" aria-label="Quitar plantilla">
                        <i data-lucide="trash-2"></i>
                    </button>` : ""}
                </article>`;

            vista.innerHTML = `
                <div class="module-heading">
                    <div>
                        <h1>Bot y derivaciones</h1>
                        <p>Configura respuestas y derivaciones por canal conectado.</p>
                    </div>
                </div>
                ${aviso}
                <section class="bot-channel-section">
                    <div>
                        <span class="panel-kicker">Canal activo</span>
                        <h2>${escapeHtml(canalActivo.nombre)}</h2>
                        <p>${escapeHtml(canalActivo.descripcion)}</p>
                    </div>
                    <div class="bot-channel-grid compact">
                        ${canalesBot.map(canal => `
                            <button type="button" class="bot-channel-card ${canal.listo ? "ready" : "warning"} ${canal.id === botCanalActivo ? "active" : ""}" data-bot-channel="${canal.id}" ${canal.soportado ? "" : "disabled"}>
                                ${crearLogoRed(canal.clase)}
                                <div>
                                    <strong>${escapeHtml(canal.nombre)}</strong>
                                    <span>${escapeHtml(canal.estado)}</span>
                                </div>
                            </button>`).join("")}
                    </div>
                </section>
                <section class="bot-layout">
                    <article class="bot-card">
                        <div class="bot-card-header">
                            <div class="bot-icon"><i data-lucide="bot-message-square"></i></div>
                            <div>
                                <h2>Respuesta automática multicanal</h2>
                                <p>Esta configuración se comparte entre WhatsApp, Facebook e Instagram. Cada canal responde cuando su conexión está completa.</p>
                            </div>
                        </div>
                        <form id="botSettingsForm" class="bot-form">
                            <label class="switch-row">
                                <span>
                                    <strong>Activar bot</strong>
                                    <small>Activa la respuesta global. Los chats pausados por un asesor se reactivan con el botón de abajo.</small>
                                </span>
                                <input id="botEnabled" type="checkbox" ${bot.enabled ? "checked" : ""} ${puedeConfigurar ? "" : "disabled"}>
                            </label>
                            <label>
                                <span>Mensaje para el cliente</span>
                                <textarea id="botMessage" rows="5" maxlength="500" ${puedeConfigurar ? "" : "disabled"}>${escapeHtml(bot.message || "")}</textarea>
                            </label>
                            <label>
                                <span>Límite de respuestas automáticas por conversación</span>
                                <input id="botMaxReplies" type="number" min="1" max="5" value="${Number(bot.maxAutoReplies || 2)}" ${puedeConfigurar ? "" : "disabled"}>
                            </label>
                            ${puedeConfigurar ? '<button type="submit" class="connection-action">Guardar configuración</button><button type="button" id="reactivateBotConversations" class="connection-action secondary">Reactivar bot en chats pausados</button>' : ""}
                        </form>
                    </article>
                    <article class="bot-card">
                        <span class="panel-kicker">Derivación económica</span>
                        <div class="bot-flow">
                            <div><strong>1</strong><span>Cliente escribe por WhatsApp, Facebook o Instagram.</span></div>
                            <div><strong>2</strong><span>El CRM crea/actualiza cliente y conversación.</span></div>
                            <div><strong>3</strong><span>Se asigna automáticamente al asesor con menos carga.</span></div>
                            <div><strong>4</strong><span>El bot envía un solo menú corto de opciones.</span></div>
                            <div><strong>5</strong><span>Si una opción deriva a asesor, el bot confirma y se pausa en ese chat.</span></div>
                            <div><strong>6</strong><span>El asesor continúa desde Comunicaciones con respuestas rápidas.</span></div>
                        </div>
                    </article>
                </section>
                <section class="bot-channel-section">
                    <div>
                        <span class="panel-kicker">Plantillas del bot</span>
                        <h2>Opciones que puede responder el cliente</h2>
                    </div>
                    <div id="botTemplatesList" class="bot-template-list">
                        ${opcionesBot.map(crearFilaPlantillaBot).join("")}
                    </div>
                    ${puedeConfigurar ? `<button id="addBotTemplate" type="button" class="bot-template-add">
                        <i data-lucide="plus"></i>
                        <span>Agregar plantilla</span>
                    </button>` : ""}
                </section>
                <section class="bot-channel-section">
                    <div>
                        <span class="panel-kicker">Plantillas rápidas</span>
                        <h2>Respuestas manuales para asesores</h2>
                    </div>
                    <div id="quickTemplatesList" class="bot-template-list">
                        ${(plantillasRapidas.length ? plantillasRapidas : [
                            { title: "Saludo", category: "Atención", message: "Hola, gracias por escribirnos. Soy tu asesor, cuéntame en qué puedo ayudarte.", enabled: true }
                        ]).map(crearFilaPlantillaRapida).join("")}
                    </div>
                    ${puedeConfigurar ? `<div class="connection-actions-row">
                        <button id="addQuickTemplate" type="button" class="bot-template-add">
                            <i data-lucide="plus"></i>
                            <span>Agregar respuesta rápida</span>
                        </button>
                        <button id="saveQuickTemplates" type="button" class="connection-action">Guardar respuestas rápidas</button>
                    </div>` : ""}
                </section>
                <section class="bot-channel-section">
                    <div>
                        <span class="panel-kicker">Canales de mensajería</span>
                        <h2>Automatización y disponibilidad</h2>
                    </div>
                    <div class="bot-channel-grid">
                        ${canalesBot.map(canal => `
                            <article class="bot-channel-card ${canal.listo ? "ready" : "warning"}">
                                ${crearLogoRed(canal.clase)}
                                <div>
                                    <strong>${escapeHtml(canal.nombre)}</strong>
                                    <span>${escapeHtml(canal.estado)}</span>
                                </div>
                                <p>${escapeHtml(canal.descripcion)}</p>
                            </article>`).join("")}
                    </div>
                </section>`;

            if (window.lucide) window.lucide.createIcons();

            vista.querySelectorAll("[data-bot-channel]").forEach(button => {
                button.addEventListener("click", async () => {
                    botCanalActivo = button.dataset.botChannel || "whatsapp";
                    await cargarModuloBot(vista);
                });
            });

            const leerPlantillasBot = () => Array
                .from(vista.querySelectorAll("[data-bot-template-row]"))
                .map(row => ({
                    key: row.querySelector("[data-template-key]").value.trim(),
                    title: row.querySelector("[data-template-title]").value.trim(),
                    response: row.querySelector("[data-template-response]").value.trim(),
                    derivesToAdvisor: row.querySelector("[data-template-derives]").checked
                }))
                .filter(option => option.key && option.title && option.response);
            const leerPlantillasRapidas = () => Array
                .from(vista.querySelectorAll("[data-quick-template-row]"))
                .map(row => ({
                    title: row.querySelector("[data-quick-title]").value.trim(),
                    category: row.querySelector("[data-quick-category]").value.trim(),
                    message: row.querySelector("[data-quick-message]").value.trim(),
                    enabled: row.querySelector("[data-quick-enabled]").checked
                }))
                .filter(template => template.title && template.message);

            vista.querySelector("#addBotTemplate")?.addEventListener("click", () => {
                const lista = vista.querySelector("#botTemplatesList");
                const cantidad = lista.querySelectorAll("[data-bot-template-row]").length + 1;
                lista.insertAdjacentHTML("beforeend", crearFilaPlantillaBot({
                    key: String(cantidad),
                    title: "Nueva opción",
                    response: "Gracias por escribirnos. Un asesor te ayudará con esta solicitud.",
                    derivesToAdvisor: true
                }));
                if (window.lucide) window.lucide.createIcons();
            });

            vista.querySelector("#botTemplatesList").addEventListener("click", event => {
                const botonQuitar = event.target.closest("[data-remove-template]");
                if (!botonQuitar) return;
                const filas = vista.querySelectorAll("[data-bot-template-row]");
                if (filas.length <= 1) {
                    notificar("Debe quedar al menos una plantilla.", "error");
                    return;
                }
                botonQuitar.closest("[data-bot-template-row]")?.remove();
            });
            vista.querySelector("#addQuickTemplate")?.addEventListener("click", () => {
                vista.querySelector("#quickTemplatesList").insertAdjacentHTML("beforeend", crearFilaPlantillaRapida({
                    title: "Nueva respuesta",
                    category: "General",
                    message: "Gracias por escribirnos. Enseguida te ayudo con tu solicitud.",
                    enabled: true
                }));
                if (window.lucide) window.lucide.createIcons();
            });
            vista.querySelector("#quickTemplatesList").addEventListener("click", event => {
                const botonQuitar = event.target.closest("[data-remove-quick-template]");
                if (!botonQuitar) return;
                botonQuitar.closest("[data-quick-template-row]")?.remove();
            });
            vista.querySelector("#saveQuickTemplates")?.addEventListener("click", async () => {
                const templates = leerPlantillasRapidas();
                if (!templates.length) {
                    notificar("Agrega al menos una respuesta rápida válida.", "error");
                    return;
                }

                const guardar = await api("/api/plantillas-rapidas", {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ templates })
                });

                if (!guardar.ok) {
                    notificar("No se pudieron guardar las respuestas rápidas.", "error");
                    return;
                }

                plantillasRapidasCache = null;
                notificar("Respuestas rápidas guardadas.", "success");
                await cargarModuloBot(vista);
            });

            vista.querySelector("#botSettingsForm")?.addEventListener("submit", async event => {
                event.preventDefault();
                const options = leerPlantillasBot();
                if (!options.length) {
                    notificar("Agrega al menos una plantilla válida para el bot.", "error");
                    return;
                }

                const guardar = await api("/api/bot/whatsapp", {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                        enabled: vista.querySelector("#botEnabled").checked,
                        message: vista.querySelector("#botMessage").value,
                        maxAutoReplies: Number(vista.querySelector("#botMaxReplies").value || 2),
                        options
                    })
                });

                if (!guardar.ok) {
                    notificar("No se pudo guardar la configuración del bot.", "error");
                    return;
                }

                notificar("Configuración del bot guardada.", "success");
                await cargarModuloBot(vista);
            });

            vista.querySelector("#reactivateBotConversations")?.addEventListener("click", async () => {
                const response = await api("/api/bot/whatsapp/reactivar-conversaciones", {
                    method: "POST"
                });
                if (!response.ok) {
                    notificar("No se pudo reactivar el bot en los chats.", "error");
                    return;
                }
                const data = await response.json();
                notificar(`Bot reactivado en ${data.actualizadas || 0} chat(s).`, "success");
                await cargarConversaciones();
                if (conversacionSeleccionada?.id) {
                    await seleccionarConversacion(conversacionSeleccionada.id, { refrescarFicha: false });
                }
            });
        }
