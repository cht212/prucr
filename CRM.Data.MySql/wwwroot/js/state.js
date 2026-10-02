// Módulo frontend del CRM.

// Respaldo para navegadores/proxies que interrumpan SSE. Los cambios normales
// llegan por /api/realtime/events y ya no requieren consultar cada 15 segundos.
const POLLING_MS = 60000;
        let conversaciones = [];
        let inboxPagina = 1;
        let inboxPaginacion = { total: 0, page: 1, pageSize: 50 };
        let conversacionSeleccionada = null;
        let actualizacionEnCurso = false;
        let filtroActivo = "all";
        let asesorFiltroActivo = "";
        let rolActual = "";
        let sesionActual = null;
        let fichaTabActiva = "datos";
        let fichaModoActivo = "completa";
        let usuariosCache = null;
        let dashboardCanalActivo = "TODOS";
        let facebookDateRange = { desde: "", hasta: "" };
        let inboxCanalActivo = "TODOS";
        let tareasFiltroActivo = "mias";
        let ventasEtapaFiltro = "TODAS";
        let reportesFiltros = { desde: "", hasta: "", usuarioId: "" };
        let marketingFiltros = { desde: "", hasta: "", canal: "TODOS" };
        let botCanalActivo = "whatsapp";
        let comunicacionesMenuAbierto = false;
        let moduloActual = "dashboard";
        let notificacionesConversacionesInicializadas = false;
        let notificacionesCentro = [];
        let notificacionesNoLeidas = 0;
        let plantillasRapidasCache = null;
        let tareasVencidasNotificadas = false;
        const ultimosMensajesCliente = new Map();
        // Módulo al que debe volver el botón "<" cuando se abre
        // una conversación individual desde Leads (o cualquier
        // otro listado que no sea el Inbox general).
        let moduloRetornoDetalle = "leads";
        let modoDetalleConversacion = false;
        let fichaClienteColapsada = false;
        let envioEnCurso = false;
        let archivosPendientes = [];
        let mensajeRespuestaSeleccionado = null;

        const lista = document.getElementById("conversationList");
        const mensajes = document.getElementById("messages");
        const estado = document.getElementById("status");
        const input = document.getElementById("messageInput");
        const boton = document.getElementById("sendButton");
        const quickReplies = document.getElementById("quickReplies");
        const fileInput = document.getElementById("fileInput");
        const attachButton = document.getElementById("attachButton");
        const replyPreview = document.getElementById("replyPreview");
        const fileName = document.getElementById("fileName");
        const attachmentPreview = document.getElementById("attachmentPreview");
        const attachmentList = document.getElementById("attachmentList");
        const logoutButton = document.getElementById("logoutButton");
        const profileButton = document.getElementById("profileButton");
        const profileDropdown = document.getElementById("profileDropdown");
        const profilePhotoInput = document.getElementById("profilePhotoInput");
        const changePhotoButton = document.getElementById("changePhotoButton");
        const viewProfileButton = document.getElementById("viewProfileButton");
        const notificationButton = document.getElementById("notificationButton");
        const notificationDropdown = document.getElementById("notificationDropdown");
        const notificationBadge = document.getElementById("notificationBadge");
        const notificationList = document.getElementById("notificationList");
        const clearNotificationsButton = document.getElementById("clearNotificationsButton");
        const markNotificationsReadButton = document.getElementById("markNotificationsReadButton");
        const modalHost = document.getElementById("modalHost");
        const nav = document.querySelector(".workspace-nav");
        const navBrandMark = document.getElementById("navBrandMark");
        const navToggle = document.getElementById("navToggle");
