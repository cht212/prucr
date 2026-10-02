// Administración de usuarios, roles y permisos.

let userDialogKeydownHandler = null;
let userDialogOpener = null;

function cerrarDialogoUsuario() {
    if (userDialogKeydownHandler) {
        document.removeEventListener("keydown", userDialogKeydownHandler);
        userDialogKeydownHandler = null;
    }
    const opener = userDialogOpener;
    userDialogOpener = null;
    delete modalHost.dataset.userDialogUserId;
    cerrarModal();
    if (opener?.isConnected) opener.focus();
}

function mostrarDialogoUsuario({ titulo, descripcion, contenido, usuarioId, opener, amplio = false }) {
    cerrarDialogoUsuario();
    userDialogOpener = opener || null;
    if (usuarioId != null) modalHost.dataset.userDialogUserId = String(usuarioId);
    modalHost.innerHTML = `
        <div class="modal-backdrop user-dialog-backdrop" data-user-dialog-close></div>
        <section id="userAdminDialog" class="crm-modal user-admin-dialog ${amplio ? "is-wide" : ""}" role="dialog" aria-modal="true" aria-labelledby="userDialogTitle">
            <div class="crm-modal-head user-admin-dialog-head">
                <div><span class="panel-kicker">Administración</span><h2 id="userDialogTitle">${escapeHtml(titulo)}</h2><p>${escapeHtml(descripcion)}</p></div>
                <button type="button" class="modal-close" data-user-dialog-close aria-label="Cerrar"><i data-lucide="x" aria-hidden="true"></i></button>
            </div>
            <div class="crm-modal-body user-admin-dialog-body">${contenido}</div>
        </section>`;
    modalHost.classList.remove("hidden");
    modalHost.setAttribute("aria-hidden", "false");
    window.lucide?.createIcons(modalHost);
    modalHost.querySelectorAll("[data-user-dialog-close]").forEach(button => button.addEventListener("click", cerrarDialogoUsuario));
    userDialogKeydownHandler = event => {
        if (event.key === "Escape") {
            event.preventDefault();
            cerrarDialogoUsuario();
        }
    };
    document.addEventListener("keydown", userDialogKeydownHandler);
    modalHost.querySelector("input, select, button:not([data-user-dialog-close])")?.focus();
}

function opcionesRoles(rolesData, selectedValue = "") {
    const bases = (rolesData?.bases || []).map(item => typeof item === "string" ? { nombre: item } : item);
    const baseOptions = bases.map(item => {
        const value = `base:${item.nombre}`;
        return `<option value="${escapeAttribute(value)}" ${selectedValue === value ? "selected" : ""}>${escapeHtml(item.nombre)}</option>`;
    }).join("");
    const customOptions = (rolesData?.roles || []).map(item => {
        const value = `custom:${item.id}`;
        return `<option value="${value}" ${selectedValue === value ? "selected" : ""}>${escapeHtml(item.nombre)}</option>`;
    }).join("");
    return `<optgroup label="Roles configurables">${customOptions || '<option disabled>Crea un rol para asignarlo</option>'}</optgroup><optgroup label="Roles del sistema">${baseOptions}</optgroup>`;
}

function datosRolSeleccionado(value, rolesData) {
    if (String(value).startsWith("custom:")) {
        const rolId = Number(String(value).slice(7));
        const role = (rolesData.roles || []).find(item => item.id === rolId);
        return { rolId, rol: role?.rolBase || "Asesor" };
    }
    return { rolId: null, rol: String(value).replace("base:", "") || "Asesor" };
}

function renderPermissionSections(catalogo, seleccionados = [], heredados = []) {
    const selected = new Set(seleccionados);
    const inherited = new Set(heredados);
    const groups = new Map();
    (catalogo || []).forEach(item => {
        const group = item.grupo || "Otros";
        if (!groups.has(group)) groups.set(group, []);
        groups.get(group).push(item);
    });
    return Array.from(groups, ([group, items], index) => {
        const moduleItem = items.find(item => String(item.codigo).startsWith("modulo."));
        const childItems = items.filter(item => item !== moduleItem);
        const titleId = `permission-section-title-${index}`;
        return `
        <section class="permission-section" data-permission-section aria-labelledby="${titleId}">
            <div class="permission-section-header">
                <h4 id="${titleId}">${escapeHtml(group)}</h4>
                ${moduleItem ? `<label class="permission-module-toggle">
                    <input type="checkbox" name="permiso" value="${escapeAttribute(moduleItem.codigo)}" data-module-permission ${selected.has(moduleItem.codigo) ? "checked" : ""}>
                    <span>Mostrar módulo</span>
                    ${inherited.has(moduleItem.codigo) ? "<em>Incluido por el rol</em>" : ""}
                </label>` : ""}
            </div>
            ${childItems.length ? `<div class="permissions-grid">${childItems.map(item => `<label class="permission-option permission-child">
                <input type="checkbox" name="permiso" value="${escapeAttribute(item.codigo)}" data-child-permission ${selected.has(item.codigo) ? "checked" : ""}>
                <span><strong>${escapeHtml(item.nombre)}</strong><small>${escapeHtml(item.descripcion)}</small>${inherited.has(item.codigo) ? "<em>Incluido por el rol</em>" : ""}</span>
            </label>`).join("")}</div>` : '<p class="permission-section-empty">Este módulo no tiene permisos adicionales.</p>'}
            ${group === "Usuarios" ? '<p class="permission-section-note">La creación de cuentas y roles continúa reservada al Administrador.</p>' : ""}
        </section>`;
    }).join("");
}

function activarLogicaPermisos(scope) {
    const sync = (section, selectChildren = false) => {
        const modulePermission = section.querySelector("[data-module-permission]");
        const children = section.querySelectorAll("[data-child-permission]");
        if (!modulePermission) return;
        if (selectChildren && modulePermission.checked) children.forEach(input => { input.checked = true; });
        children.forEach(input => {
            input.disabled = !modulePermission.checked;
            if (!modulePermission.checked) input.checked = false;
        });
        section.classList.toggle("module-disabled", !modulePermission.checked);
    };
    scope.querySelectorAll("[data-permission-section]").forEach(section => {
        sync(section);
        section.querySelector("[data-module-permission]")?.addEventListener("change", () => sync(section, true));
    });
    return () => scope.querySelectorAll("[data-permission-section]").forEach(section => sync(section));
}

let usuariosPagina = 1;
let usuariosBusqueda = "";
let usuariosCargaVersion = 0;
async function cargarModuloUsuarios(vista) {
    const carga = ++usuariosCargaVersion;
    const search = usuariosBusqueda;
    const enfocarBusqueda = document.activeElement?.id === "usersSearch";
    if (!puedeVerModulo("usuarios")) {
        vista.innerHTML = '<div class="error">No tienes permiso para ver usuarios.</div>';
        return;
    }

    const puedeAdministrar = esRol("administrador");
    const [usersResponse, rolesResponse] = await Promise.all([
        api(`/api/crm/usuarios?page=${usuariosPagina}&pageSize=${obtenerTamanoPagina("usuarios")}&search=${encodeURIComponent(search)}`),
        puedeAdministrar ? api("/api/crm/roles") : Promise.resolve(null)
    ]);
    if (!usersResponse.ok) throw new Error("Usuarios no disponibles");
    const data = await usersResponse.json();
    const size = obtenerTamanoPagina("usuarios");
    const pagina = Array.isArray(data) ? { total: data.length, page: usuariosPagina, pageSize: size,
        items: data.slice((usuariosPagina - 1) * size, usuariosPagina * size),
        resumen: { activos: data.length, configurables: data.filter(u => normalizarRol(u.rolBase || u.rol) !== "administrador").length } } : data;
    const usuarios = pagina.items || [];
    const rolesData = rolesResponse?.ok ? await rolesResponse.json() : { bases: [], catalogo: [], roles: [] };
    if (carga !== usuariosCargaVersion || search !== usuariosBusqueda || moduloActual !== "usuarios") return;
    const rolesCount = (rolesData.roles || []).length;

    vista.innerHTML = `
        <div class="module-heading users-page-heading">
            <div><h1>Usuarios y roles</h1><p>Gestiona las cuentas del equipo y define con claridad a qué puede acceder cada perfil.</p></div>
            ${puedeAdministrar ? `<div class="heading-actions"><button id="openRoleCreate" class="secondary-btn" type="button"><i data-lucide="shield-plus"></i><span>Nuevo rol</span></button><button id="openUserCreate" class="primary-action" type="button"><i data-lucide="user-plus"></i><span>Nuevo usuario</span></button></div>` : ""}
        </div>
        <div class="admin-summary-grid">
            <article><span class="summary-icon"><i data-lucide="users-round"></i></span><div><strong>${pagina.resumen?.activos ?? pagina.total}</strong><span>Usuarios activos</span></div></article>
            <article><span class="summary-icon is-purple"><i data-lucide="shield-check"></i></span><div><strong>${rolesCount}</strong><span>Roles configurables</span></div></article>
            <article><span class="summary-icon is-green"><i data-lucide="circle-check-big"></i></span><div><strong>${pagina.resumen?.configurables ?? usuarios.filter(item => item.rolBase !== "Administrador").length}</strong><span>Cuentas configurables</span></div></article>
        </div>
        <section class="admin-list-card">
            <div class="module-card-heading"><div><h2>Equipo</h2><p>Usuarios con acceso al CRM</p></div><label class="compact-search"><i data-lucide="search"></i><input id="usersSearch" type="search" placeholder="Buscar usuario"></label></div>
            ${!puedeAdministrar ? '<div class="connection-note"><strong>Solo lectura:</strong> puedes consultar las cuentas, pero no modificar sus accesos.</div>' : ""}
            <div class="table-wrap"><table class="module-table users-table">
                <thead><tr><th>Usuario</th><th>Nombre</th><th>Rol asignado</th><th>Acciones</th></tr></thead>
                <tbody>${usuarios.map(usuario => `<tr data-user-row data-search="${escapeAttribute(`${usuario.usuario} ${usuario.nombre} ${usuario.rol}`.toLowerCase())}">
                    <td><div class="user-identity"><span class="user-avatar">${escapeHtml((usuario.nombre || usuario.usuario).charAt(0).toUpperCase())}</span><div><strong>${escapeHtml(usuario.usuario)}</strong><small>Cuenta de acceso</small></div></div></td>
                    <td>${escapeHtml(usuario.nombre)}</td>
                    <td><span class="role-badge">${escapeHtml(usuario.rol)}</span></td>
                    <td>${puedeAdministrar && normalizarRol(usuario.rolBase) !== "administrador" ? `<div class="table-actions user-row-actions"><button type="button" class="table-icon-action" data-user-edit="${usuario.id}" title="Editar cuenta" aria-label="Editar cuenta de ${escapeAttribute(usuario.nombre)}"><i data-lucide="edit"></i></button><button type="button" class="table-icon-action" data-user-permissions="${usuario.id}" title="Permisos especiales" aria-label="Configurar permisos de ${escapeAttribute(usuario.nombre)}"><i data-lucide="key-round"></i></button><button type="button" class="table-icon-action is-danger" data-user-delete="${usuario.id}" title="Eliminar usuario" aria-label="Eliminar usuario ${escapeAttribute(usuario.nombre)}"><i data-lucide="trash-2"></i></button></div>` : '<span class="account-owner-label" title="Cuenta principal protegida"><i data-lucide="crown"></i><span>Principal</span></span>'}</td>
                </tr>`).join("") || '<tr><td colspan="4">No hay usuarios.</td></tr>'}</tbody>
            </table></div>
            ${renderPaginacion(pagina, "usuarios")}
        </section>
        ${puedeAdministrar ? `<section class="roles-section"><div class="section-heading"><div><span class="panel-kicker">Control de acceso</span><h2>Roles</h2><p>Crea perfiles independientes y define exactamente sus permisos.</p></div></div><div class="role-cards">${(rolesData.roles || []).map(role => `<article class="role-card"><div class="role-card-top"><span class="role-card-icon"><i data-lucide="shield"></i></span><div class="role-card-actions"><button type="button" class="table-icon-action" data-role-edit="${role.id}" title="Editar rol" aria-label="Editar rol ${escapeAttribute(role.nombre)}"><i data-lucide="edit-3"></i></button><button type="button" class="table-icon-action danger" data-role-delete="${role.id}" title="Eliminar rol" aria-label="Eliminar rol ${escapeAttribute(role.nombre)}"><i data-lucide="trash-2"></i></button></div></div><h3>${escapeHtml(role.nombre)}</h3><p>${escapeHtml(role.descripcion || "Sin descripción")}</p><div class="role-card-meta"><span>${role.usuarios} usuario${role.usuarios === 1 ? "" : "s"}</span><span>${(role.permisos || []).length} permisos</span></div></article>`).join("") || '<button id="emptyRoleCreate" class="empty-role-card" type="button"><i data-lucide="shield-plus"></i><strong>Crea tu primer rol</strong><span>Define permisos reutilizables para el equipo.</span></button>'}</div></section>` : ""}`;

    window.lucide?.createIcons(vista);
    enlazarPaginacion(vista, page => { usuariosPagina = page; return cargarModuloUsuarios(vista); });
    const inputBusqueda = vista.querySelector("#usersSearch");
    inputBusqueda.value = usuariosBusqueda;
    if (enfocarBusqueda && (document.activeElement === document.body || document.activeElement?.id === "usersSearch")) inputBusqueda.focus();
    let busquedaTimer;
    vista.querySelector("#usersSearch")?.addEventListener("input", event => {
        usuariosBusqueda = event.currentTarget.value;
        usuariosPagina = 1;
        clearTimeout(busquedaTimer);
        busquedaTimer = setTimeout(() => cargarModuloUsuarios(vista).catch(error => {
            if (error?.name !== "AbortError") notificar("No se pudo buscar usuarios.", "error");
        }), 250);
    });
    vista.querySelector("#openUserCreate")?.addEventListener("click", event => abrirCreacionUsuario(vista, rolesData, event.currentTarget));
    vista.querySelector("#openRoleCreate")?.addEventListener("click", event => abrirEdicionRol(vista, rolesData, null, event.currentTarget));
    vista.querySelector("#emptyRoleCreate")?.addEventListener("click", event => abrirEdicionRol(vista, rolesData, null, event.currentTarget));
    vista.querySelectorAll("[data-user-edit]").forEach(button => {
        const usuario = usuarios.find(item => String(item.id) === button.dataset.userEdit);
        button.addEventListener("click", () => abrirEdicionUsuario(vista, usuario, rolesData, button));
    });
    vista.querySelectorAll("[data-user-permissions]").forEach(button => button.addEventListener("click", () => abrirPermisosUsuario(vista, button.dataset.userPermissions, button)));
    vista.querySelectorAll("[data-user-delete]").forEach(button => {
        const usuario = usuarios.find(item => String(item.id) === button.dataset.userDelete);
        button.addEventListener("click", () => eliminarUsuario(vista, usuario, button));
    });
    vista.querySelectorAll("[data-role-edit]").forEach(button => {
        const role = (rolesData.roles || []).find(item => String(item.id) === button.dataset.roleEdit);
        button.addEventListener("click", () => abrirEdicionRol(vista, rolesData, role, button));
    });
    vista.querySelectorAll("[data-role-delete]").forEach(button => {
        const role = (rolesData.roles || []).find(item => String(item.id) === button.dataset.roleDelete);
        button.addEventListener("click", () => eliminarRol(vista, role, button));
    });
}

async function eliminarUsuario(vista, usuario, button) {
    if (!usuario) return;
    if (!window.confirm(`¿Eliminar el acceso de ${usuario.nombre}? El usuario ya no podrá iniciar sesión.`)) return;
    button.disabled = true;
    try {
        const response = await api(`/api/crm/usuarios/${usuario.id}`, { method: "DELETE" });
        if (!response.ok) { notificar(await response.text(), "error"); return; }
        usuariosCache = null;
        await cargarModuloUsuarios(vista);
        notificar("Usuario eliminado correctamente.", "success");
    } finally { button.disabled = false; }
}

function abrirCreacionUsuario(vista, rolesData, opener) {
    mostrarDialogoUsuario({
        titulo: "Nuevo usuario",
        descripcion: "Crea una cuenta y asígnale un rol. Los permisos se pueden ajustar después.",
        opener,
        contenido: `<form id="newUserForm" class="entity-dialog-form">
            <label><span>Usuario de acceso</span><input name="usuario" maxlength="50" placeholder="Ej. mgonzales" autocomplete="username" required></label>
            <label><span>Nombre completo</span><input name="nombre" maxlength="150" placeholder="Nombre y apellidos" autocomplete="name" required></label>
            <label><span>Contraseña inicial</span><input name="password" type="password" minlength="8" placeholder="Mínimo 8 caracteres" autocomplete="new-password" required></label>
            <label><span>Rol</span><select name="roleChoice" required>${opcionesRoles(rolesData, rolesData.roles?.length ? `custom:${rolesData.roles[0].id}` : "base:Asesor")}</select><small class="field-help">El rol define los accesos iniciales de la cuenta.</small></label>
            <div class="entity-dialog-actions"><button type="button" class="ghost-button" data-user-dialog-close>Cancelar</button><button type="submit" class="primary">Crear usuario</button></div>
        </form>`
    });
    const form = modalHost.querySelector("#newUserForm");
    form?.addEventListener("submit", async event => {
        event.preventDefault();
        const submit = form.querySelector('button[type="submit"]');
        const raw = Object.fromEntries(new FormData(form));
        const role = datosRolSeleccionado(raw.roleChoice, rolesData);
        submit.disabled = true;
        try {
            const response = await api("/api/crm/usuarios", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ usuario: raw.usuario, nombre: raw.nombre, password: raw.password, ...role }) });
            if (!response.ok) { notificar(await response.text(), "error"); return; }
            cerrarDialogoUsuario();
            usuariosCache = null;
            await cargarModuloUsuarios(vista);
            notificar("Usuario creado correctamente.", "success");
        } finally { submit.disabled = false; }
    });
}

function abrirEdicionUsuario(vista, usuario, rolesData, opener) {
    if (!usuario) return;
    const selectedRole = usuario.rolId ? `custom:${usuario.rolId}` : `base:${usuario.rolBase}`;
    mostrarDialogoUsuario({
        titulo: `Editar cuenta · ${usuario.usuario}`,
        descripcion: "Actualiza los datos de la cuenta. Cambiar el rol restablece sus ajustes especiales.",
        opener,
        contenido: `<form id="userEditForm" class="entity-dialog-form">
            <label><span>Nombre completo</span><input name="nombre" value="${escapeAttribute(usuario.nombre)}" required maxlength="150"></label>
            <label><span>Rol</span><select name="roleChoice" required>${opcionesRoles(rolesData, selectedRole)}</select></label>
            <label class="entity-dialog-wide"><span>Nueva contraseña <small>Opcional</small></span><input name="password" type="password" placeholder="Déjala vacía para conservar la actual" minlength="8" autocomplete="new-password"></label>
            <div class="entity-dialog-actions"><button type="button" class="ghost-button" data-user-dialog-close>Cancelar</button><button type="submit" class="primary">Guardar cambios</button></div>
        </form>`
    });
    const form = modalHost.querySelector("#userEditForm");
    form?.addEventListener("submit", async event => {
        event.preventDefault();
        const submit = form.querySelector('button[type="submit"]');
        const raw = Object.fromEntries(new FormData(form));
        const role = datosRolSeleccionado(raw.roleChoice, rolesData);
        submit.disabled = true;
        try {
            const response = await api(`/api/crm/usuarios/${usuario.id}`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ nombre: raw.nombre, password: raw.password, ...role }) });
            if (!response.ok) { notificar(await response.text(), "error"); return; }
            cerrarDialogoUsuario();
            usuariosCache = null;
            await cargarModuloUsuarios(vista);
            notificar("Cuenta actualizada.", "success");
        } finally { submit.disabled = false; }
    });
}

function abrirEdicionRol(vista, rolesData, role, opener) {
    const selected = role?.permisos || [];
    mostrarDialogoUsuario({
        titulo: role ? `Editar rol · ${role.nombre}` : "Nuevo rol",
        descripcion: "Define un rol independiente y selecciona exactamente qué apartados y acciones habilita.",
        opener,
        amplio: true,
        contenido: `<form id="roleForm" class="role-editor-form">
            <div class="role-main-fields"><label><span>Nombre del rol</span><input name="nombre" maxlength="80" value="${escapeAttribute(role?.nombre || "")}" placeholder="Ej. Gerencia" required></label><label><span>Descripción</span><input name="descripcion" maxlength="250" value="${escapeAttribute(role?.descripcion || "")}" placeholder="Responsabilidades principales de este perfil"></label></div>
            <div class="role-permissions-heading"><div><h3>Permisos del rol</h3><p>Activa los módulos y las acciones que tendrá este rol.</p></div></div>
            <div class="permission-sections">${renderPermissionSections(rolesData.catalogo, selected)}</div>
            <div class="entity-dialog-actions sticky"><button type="button" class="ghost-button" data-user-dialog-close>Cancelar</button><button type="submit" class="primary">${role ? "Guardar rol" : "Crear rol"}</button></div>
        </form>`
    });
    const form = modalHost.querySelector("#roleForm");
    activarLogicaPermisos(form);
    form.addEventListener("submit", async event => {
        event.preventDefault();
        const submit = form.querySelector('button[type="submit"]');
        const raw = Object.fromEntries(new FormData(form));
        const permisos = Array.from(form.querySelectorAll('input[name="permiso"]:checked')).map(input => input.value);
        submit.disabled = true;
        try {
            const response = await api(role ? `/api/crm/roles/${role.id}` : "/api/crm/roles", { method: role ? "PUT" : "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ nombre: raw.nombre, descripcion: raw.descripcion, permisos }) });
            if (!response.ok) { notificar(await response.text(), "error"); return; }
            cerrarDialogoUsuario();
            usuariosCache = null;
            await cargarModuloUsuarios(vista);
            notificar(role ? "Rol actualizado." : "Rol creado correctamente.", "success");
        } finally { submit.disabled = false; }
    });
}

async function eliminarRol(vista, role, button) {
    if (!role) return;
    if (role.usuarios > 0) {
        notificar(`No puedes eliminar ${role.nombre} porque tiene usuarios asignados. Reasígnalos primero.`, "error");
        return;
    }
    if (!window.confirm(`¿Eliminar el rol ${role.nombre}? Esta acción no se puede deshacer.`)) return;
    button.disabled = true;
    try {
        const response = await api(`/api/crm/roles/${role.id}`, { method: "DELETE" });
        if (!response.ok) { notificar(await response.text(), "error"); return; }
        usuariosCache = null;
        await cargarModuloUsuarios(vista);
        notificar("Rol eliminado correctamente.", "success");
    } finally { button.disabled = false; }
}

async function abrirPermisosUsuario(vista, usuarioId, opener) {
    mostrarDialogoUsuario({ titulo: "Permisos especiales", descripcion: "Cargando la configuración del usuario...", usuarioId, opener, amplio: true, contenido: '<div class="empty">Cargando seguridad...</div>' });
    const response = await api(`/api/crm/usuarios/${usuarioId}/permisos`);
    if (modalHost.dataset.userDialogUserId !== String(usuarioId)) return;
    const body = modalHost.querySelector(".crm-modal-body");
    if (!response.ok) { body.innerHTML = `<div class="error">${escapeHtml(await response.text())}</div>`; return; }
    const data = await response.json();
    const permissions = data.permisos || [];
    const selected = permissions.filter(item => item.efectivo).map(item => item.codigo);
    const inherited = permissions.filter(item => item.heredado).map(item => item.codigo);
    modalHost.querySelector(".user-admin-dialog-head p").textContent = `Ajusta excepciones individuales para ${data.usuario?.nombre || "este usuario"}.`;
    body.innerHTML = `<form id="userPermissionsForm" class="permissions-form"><div class="permission-context"><i data-lucide="info"></i><span>Estos cambios solo afectan a este usuario y se aplican sobre los permisos de su rol.</span></div><div class="permission-sections">${renderPermissionSections(permissions, selected, inherited)}</div><div class="entity-dialog-actions sticky"><button type="button" class="ghost-button" data-reset-permissions>Restablecer al rol</button><button type="submit" class="primary">Guardar permisos</button></div></form>`;
    window.lucide?.createIcons(body);
    const sync = activarLogicaPermisos(body);
    body.querySelector("[data-reset-permissions]")?.addEventListener("click", () => {
        const defaults = new Set(inherited);
        body.querySelectorAll('input[name="permiso"]').forEach(input => input.checked = defaults.has(input.value));
        sync();
    });
    body.querySelector("#userPermissionsForm")?.addEventListener("submit", async event => {
        event.preventDefault();
        const submit = event.currentTarget.querySelector('button[type="submit"]');
        const permisos = Array.from(event.currentTarget.querySelectorAll('input[name="permiso"]:checked')).map(input => input.value);
        submit.disabled = true;
        try {
            const save = await api(`/api/crm/usuarios/${usuarioId}/permisos`, { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ permisos, personalizar: true }) });
            if (!save.ok) { notificar(await save.text(), "error"); return; }
            cerrarDialogoUsuario();
            notificar("Permisos del usuario actualizados.", "success");
        } finally { submit.disabled = false; }
    });
}
