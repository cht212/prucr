/*
 * Adaptador local de Remix Icon.
 *
 * El frontend de referencia usa @iconify-json/ri 1.2.6. La aplicacion
 * historica invoca `lucide.createIcons()`, por lo que conservamos esa API
 * para no mezclar la migracion visual con la logica de cada modulo.
 */
(function () {
    "use strict";

    const icons = {
        "arrow-left-s-line": '<path fill="currentColor" d="M10.8284 12.0007L15.7782 16.9504L14.364 18.3646L8 12.0007L14.364 5.63672L15.7782 7.05093L10.8284 12.0007Z"/>',
        "arrow-right-s-line": '<path fill="currentColor" d="M13.1717 12.0007L8.22192 7.05093L9.63614 5.63672L16.0001 12.0007L9.63614 18.3646L8.22192 16.9504L13.1717 12.0007Z"/>',
        "skip-back-line": '<path fill="currentColor" d="M7 4C7.55228 4 8 4.44772 8 5V11.3333L18.2227 4.51823C18.4524 4.36506 18.7628 4.42714 18.916 4.65691C18.9708 4.73904 19 4.83555 19 4.93426V19.0657C19 19.3419 18.7761 19.5657 18.5 19.5657C18.4013 19.5657 18.3048 19.5365 18.2227 19.4818L8 12.6667V19C8 19.5523 7.55228 20 7 20C6.44772 20 6 19.5523 6 19V5C6 4.44772 6.44772 4 7 4ZM17 7.73703L10.6056 12L17 16.263V7.73703Z"/>',
        "skip-forward-line": '<path fill="currentColor" d="M16 12.6667L5.77735 19.4818C5.54759 19.6349 5.23715 19.5729 5.08397 19.3431C5.02922 19.261 5 19.1645 5 19.0657V4.93426C5 4.65812 5.22386 4.43426 5.5 4.43426C5.59871 4.43426 5.69522 4.46348 5.77735 4.51823L16 11.3333V5C16 4.44772 16.4477 4 17 4C17.5523 4 18 4.44772 18 5V19C18 19.5523 17.5523 20 17 20C16.4477 20 16 19.5523 16 19V12.6667ZM7 7.73703V16.263L13.3944 12L7 7.73703Z"/>',
        "arrow-left-line": '<path fill="currentColor" d="M7.828 11H20v2H7.828l5.364 5.364l-1.414 1.414L4 12l7.778-7.778l1.414 1.414z"/>',
        "arrow-right-line": '<path fill="currentColor" d="m16.172 11l-5.364-5.364l1.414-1.414L20 12l-7.778 7.778l-1.414-1.414L16.172 13H4v-2z"/>',
        "attachment-2": '<path fill="currentColor" d="M14.828 7.757l-6.364 6.364a2 2 0 0 0 2.829 2.829l6.364-6.364a4 4 0 0 0-5.657-5.657l-6.364 6.364a6 6 0 0 0 8.485 8.485l6.364-6.364l1.414 1.414l-6.364 6.364A8 8 0 0 1 4.222 9.879l6.364-6.364a6 6 0 0 1 8.485 8.485l-6.364 6.364a4 4 0 0 1-5.657-5.657l6.364-6.364z"/>',
        "money-dollar-circle-line": '<path fill="currentColor" d="M12.005 22.003c-5.523 0-10-4.477-10-10s4.477-10 10-10s10 4.477 10 10s-4.477 10-10 10m0-2a8 8 0 1 0 0-16a8 8 0 0 0 0 16m-3.5-6h5.5a.5.5 0 1 0 0-1h-4a2.5 2.5 0 1 1 0-5h1v-2h2v2h2.5v2h-5.5a.5.5 0 0 0 0 1h4a2.5 2.5 0 0 1 0 5h-1v2h-2v-2h-2.5z"/>',
        "bar-chart-line": '<path fill="currentColor" d="M3 12h2v9H3zm16-4h2v13h-2zm-8-6h2v19h-2z"/>',
        "notification-3-line": '<path fill="currentColor" d="M20 17h2v2H2v-2h2v-7a8 8 0 1 1 16 0zm-2 0v-7a6 6 0 0 0-12 0v7zm-9 4h6v2H9z"/>',
        "robot-2-line": '<path fill="currentColor" d="M13.5 2c0 .444-.193.843-.5 1.118V5h5a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V8a3 3 0 0 1 3-3h5V3.118A1.5 1.5 0 1 1 13.5 2M6 7a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8a1 1 0 0 0-1-1zm-4 3H0v6h2zm20 0h2v6h-2zM9 14.5a1.5 1.5 0 1 0 0-3a1.5 1.5 0 0 0 0 3m6 0a1.5 1.5 0 1 0 0-3a1.5 1.5 0 0 0 0 3"/>',
        "calendar-line": '<path fill="currentColor" d="M9 1v2h6V1h2v2h4a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h4V1zm11 10H4v8h16zM7 5H4v4h16V5h-3v2h-2V5H9v2H7z"/>',
        "check-line": '<path fill="currentColor" d="m10 15.172l9.192-9.193l1.415 1.414L10 18L3.636 11.636l1.414-1.414z"/>',
        "bar-chart-box-line": '<path fill="currentColor" d="M3 3h18a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1m1 2v14h16V5zm3 8h2v4H7zm4-6h2v10h-2zm4 3h2v7h-2z"/>',
        "line-chart-line": '<path fill="currentColor" d="M5 3v16h16v2H3V3zm15.293 3.293l1.414 1.414L16 13.414l-3-2.999l-4.293 4.292l-1.414-1.414L13 7.586l3 2.999z"/>',
        "arrow-down-s-line": '<path fill="currentColor" d="m12 13.171l4.95-4.95l1.414 1.415L12 16L5.636 9.636L7.05 8.222z"/>',
        "arrow-up-s-line": '<path fill="currentColor" d="m12 10.828l-4.95 4.95l-1.414-1.414L12 8l6.364 6.364l-1.414 1.414z"/>',
        "contacts-line": '<path fill="currentColor" d="M19 7h5v2h-5zm-2 5h7v2h-7zm3 5h4v2h-4zM2 22a8 8 0 1 1 16 0h-2a6 6 0 0 0-12 0zm8-9c-3.315 0-6-2.685-6-6s2.685-6 6-6s6 2.685 6 6s-2.685 6-6 6m0-2c2.21 0 4-1.79 4-4s-1.79-4-4-4s-4 1.79-4 4s1.79 4 4 4"/>',
        "external-link-line": '<path fill="currentColor" d="M10 6v2H5v11h11v-5h2v6a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1zm11-3v8h-2V6.413l-7.793 7.794l-1.414-1.414L17.585 5H13V3z"/>',
        "eye-line": '<path fill="currentColor" d="M12 3c5.392 0 9.878 3.88 10.819 9c-.94 5.12-5.427 9-10.819 9s-9.878-3.88-10.818-9C2.122 6.88 6.608 3 12 3m0 16a9.005 9.005 0 0 0 8.778-7a9.005 9.005 0 0 0-17.555 0A9.005 9.005 0 0 0 12 19m0-2.5a4.5 4.5 0 1 1 0-9a4.5 4.5 0 0 1 0 9m0-2a2.5 2.5 0 1 0 0-5a2.5 2.5 0 0 0 0 5"/>',
        "eye-off-line": '<path fill="currentColor" d="M17.883 19.297A10.95 10.95 0 0 1 12 21c-5.392 0-9.878-3.88-10.818-9A11 11 0 0 1 4.52 5.935L1.394 2.808l1.414-1.414l19.799 19.798l-1.414 1.415zM5.936 7.35A8.97 8.97 0 0 0 3.223 12a9.005 9.005 0 0 0 13.201 5.838l-2.028-2.028A4.5 4.5 0 0 1 8.19 9.604zm6.978 6.978l-3.242-3.241a2.5 2.5 0 0 0 3.241 3.241m7.893 2.265l-1.431-1.431A8.9 8.9 0 0 0 20.778 12A9.005 9.005 0 0 0 9.552 5.338L7.974 3.76C9.221 3.27 10.58 3 12 3c5.392 0 9.878 3.88 10.819 9a10.95 10.95 0 0 1-2.012 4.593m-9.084-9.084Q11.86 7.5 12 7.5a4.5 4.5 0 0 1 4.492 4.778z"/>',
        "mail-line": '<path fill="currentColor" d="M3 3h18a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1m17 4.238l-7.928 7.1L4 7.216V19h16zM4.511 5l7.55 6.662L19.502 5z"/>',
        "phone-line": '<path fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" d="M7.2 3.5 4.6 5.1c-1.1.7-1.4 2.1-.8 3.2 2.7 5.3 6.6 9.2 11.9 11.9 1.1.6 2.5.3 3.2-.8l1.6-2.6-4.7-3-1.7 2.2c-2.6-1.4-4.7-3.5-6.1-6.1l2.2-1.7z"/>',
        "lock-line": '<path fill="currentColor" d="M19 10h1a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V11a1 1 0 0 1 1-1h1V9a7 7 0 0 1 14 0zM5 12v8h14v-8zm6 2h2v4h-2zm6-4V9A5 5 0 0 0 7 9v1z"/>',
        "login-box-line": '<path fill="currentColor" d="M4 15h2v5h12V4H6v5H4V3a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v18a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1zm6-4V8l5 4l-5 4v-3H2v-2z"/>',
        "file-line": '<path fill="currentColor" d="M9 2.003V2h10.998C20.55 2 21 2.455 21 2.992v18.016a.993.993 0 0 1-.993.992H3.993A1 1 0 0 1 3 20.993V8zM5.83 8H9V4.83zM11 4v5a1 1 0 0 1-1 1H5v10h14V4z"/>',
        "file-text-line": '<path fill="currentColor" d="M21 8v12.993A1 1 0 0 1 20.007 22H3.993A.993.993 0 0 1 3 21.008V2.992C3 2.455 3.449 2 4.002 2h10.995zm-2 1h-5V4H5v16h14zM8 7h3v2H8zm0 4h8v2H8zm0 4h8v2H8z"/>',
        "heart-line": '<path fill="currentColor" d="M12.001 4.529a6 6 0 0 1 8.242.228a6 6 0 0 1 .236 8.236l-8.48 8.492l-8.478-8.492a6 6 0 0 1 8.48-8.464m6.826 1.641a4 4 0 0 0-5.49-.153l-1.335 1.198l-1.336-1.197a4 4 0 0 0-5.686 5.605L12 18.654l7.02-7.03a4 4 0 0 0-.193-5.454"/>',
        "history-line": '<path fill="currentColor" d="M12 2c5.523 0 10 4.477 10 10s-4.477 10-10 10S2 17.523 2 12h2a8 8 0 1 0 1.385-4.5H8v2H2v-6h2V6a9.99 9.99 0 0 1 8-4m1 5v4.585l3.243 3.243l-1.415 1.415L11 12.413V7z"/>',
        "home-smile-2-line": '<path fill="currentColor" d="M19 19V9.799l-7-5.522l-7 5.522V19zm2 1a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V9.314a1 1 0 0 1 .38-.785l8-6.311a1 1 0 0 1 1.24 0l8 6.31a1 1 0 0 1 .38.786zM7 12h2a3 3 0 1 0 6 0h2a5 5 0 0 1-10 0"/>',
        "image-add-line": '<path fill="currentColor" d="M21 15v3h3v2h-3v3h-2v-3h-3v-2h3v-3zm.008-12c.548 0 .992.445.992.993V13h-2V5H4v13.999L14 9l3 3v2.829l-3-3L6.827 19H14v2H2.992A.993.993 0 0 1 2 20.007V3.993A1 1 0 0 1 2.992 3zM8 7a2 2 0 1 1 0 4a2 2 0 0 1 0-4"/>',
        "layout-grid-line": '<path fill="currentColor" d="M21 3a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zM11 13H4v6h7zm9 0h-7v6h7zm-9-8H4v6h7zm9 0h-7v6h7z"/>',
        "macbook-line": '<path fill="currentColor" d="M4 5v11h16V5zm-2-.993C2 3.451 2.455 3 2.992 3h18.016c.548 0 .992.449.992 1.007V18H2zM1 19h22v2H1z"/>',
        "list-check-3": '<path fill="currentColor" d="M8 6v3H5V6zM3 4v7h7V4zm10 0h8v2h-8zm0 7h8v2h-8zm0 7h8v2h-8zm-2.293-1.793l-1.414-1.414L6 18.086l-1.793-1.793l-1.414 1.414L6 20.914z"/>',
        "loader-4-line": '<path fill="currentColor" d="M18.364 5.636L16.95 7.05A7 7 0 1 0 19 12h2a9 9 0 1 1-2.636-6.364"/>',
        "logout-box-r-line": '<path fill="currentColor" d="M5 22a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v3h-2V4H6v16h12v-2h2v3a1 1 0 0 1-1 1zm13-6v-3h-7v-2h7V8l5 4z"/>',
        "menu-line": '<path fill="currentColor" d="M3 4h18v2H3zm0 7h18v2H3zm0 7h18v2H3z"/>',
        "chat-3-line": '<path fill="currentColor" d="M7.291 20.824L2 22l1.176-5.291A9.96 9.96 0 0 1 2 12C2 6.477 6.477 2 12 2s10 4.477 10 10s-4.477 10-10 10a9.96 9.96 0 0 1-4.709-1.176m.29-2.113l.653.35A7.96 7.96 0 0 0 12 20a8 8 0 1 0-8-8c0 1.335.325 2.617.94 3.766l.349.653l-.655 2.947z"/>',
        "chat-1-line": '<path fill="currentColor" d="M10 3h4a8 8 0 1 1 0 16v3.5c-5-2-12-5-12-11.5a8 8 0 0 1 8-8m2 14h2a6 6 0 0 0 0-12h-4a6 6 0 0 0-6 6c0 3.61 2.462 5.966 8 8.48z"/>',
        "mail-volume-line": '<path fill="currentColor" d="M20 14.5v9L16.667 21H14v-4h2.667zM21 3a1 1 0 0 1 1 1v9h-2V7.237l-7.928 7.101L4 7.215V19h8v2H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zm0 14a2 2 0 0 1 .15 3.994L21 21zM19.5 5H4.511l7.55 6.662z"/>',
        "moon-clear-line": '<path fill="currentColor" d="M10 6a8 8 0 0 0 11.955 6.956C21.474 18.03 17.2 22 12 22C6.477 22 2 17.523 2 12c0-5.2 3.97-9.474 9.044-9.955A7.96 7.96 0 0 0 10 6m-6 6a8 8 0 0 0 8 8a8 8 0 0 0 6.957-4.045Q18.482 16 18 16C12.477 16 8 11.523 8 6q0-.481.045-.957A8 8 0 0 0 4 12m14.164-9.709L19 2.5v1l-.836.209a2 2 0 0 0-1.455 1.455L16.5 6h-1l-.209-.836a2 2 0 0 0-1.455-1.455L13 3.5v-1l.836-.209A2 2 0 0 0 15.29.836L15.5 0h1l.209.836a2 2 0 0 0 1.455 1.455m5 5L24 7.5v1l-.836.209a2 2 0 0 0-1.455 1.455L21.5 11h-1l-.209-.836a2 2 0 0 0-1.455-1.455L18 8.5v-1l.836-.209a2 2 0 0 0 1.455-1.455L20.5 5h1l.209.836a2 2 0 0 0 1.455 1.455"/>',
        "cursor-line": '<path fill="currentColor" d="m15.387 13.498l2.553 7.014l-4.698 1.71l-2.553-7.014l-3.899 2.445l1.619-16.02l11.537 11.232zm-.01 5.818l-2.715-7.46l2.96-.41l-5.64-5.49l-.791 7.83l2.531-1.587l2.715 7.46z"/>',
        "sidebar-fold-line": '<path fill="currentColor" d="M5 5h8v14H5zm14 14h-4V5h4zM4 3a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1zm3 9l4-3.5v7z"/>',
        "sidebar-unfold-line": '<path fill="currentColor" d="M5 5h8v14H5zm14 14h-4V5h4zM4 3a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1zm7 9L7 8.5v7z"/>',
        "layout-right-line": '<path fill="currentColor" d="M21 3a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zm-6 2H4v14h11zm5 0h-3v14h3z"/>',
        "layout-left-line": '<path fill="currentColor" d="M21 3a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1zM7 5H4v14h3zm13 0H9v14h11z"/>',
        "pie-chart-2-line": '<path fill="currentColor" d="M12 .5C18.351.5 23.5 5.649 23.5 12q0 .505-.043 1h-1.506c-.502 5.053-4.766 9-9.951 9c-5.523 0-10-4.477-10-10c0-5.185 3.947-9.449 9-9.95V.542Q11.495.5 12 .5m-1 3.562A8.001 8.001 0 0 0 12 20a8 8 0 0 0 7.938-7H11zm2-1.51V11h8.448A9.5 9.5 0 0 0 13 2.552"/>',
        "edit-line": '<path fill="currentColor" d="M6.414 15.89L16.556 5.748l-1.414-1.414L5 14.476v1.414zm.829 2H3v-4.243L14.435 2.212a1 1 0 0 1 1.414 0l2.829 2.829a1 1 0 0 1 0 1.414zM3 19.89h18v2H3z"/>',
        "pencil-line": '<path fill="currentColor" d="m18.414 2.586l3 3a2 2 0 0 1 0 2.828L8.414 21.414A2 2 0 0 1 7 22H2v-5a2 2 0 0 1 .586-1.414l13-13a2 2 0 0 1 2.828 0M17 4L4 17v3h3L20 7zm-2 3l2 2L8 18H6v-2z"/>',
        "plug-line": '<path fill="currentColor" d="M13 18v2h6v2h-6a2 2 0 0 1-2-2v-2H8a4 4 0 0 1-4-4V7a1 1 0 0 1 1-1h3V2h2v4h4V2h2v4h3a1 1 0 0 1 1 1v7a4 4 0 0 1-4 4zm-5-2h8a2 2 0 0 0 2-2v-3H6v3a2 2 0 0 0 2 2m10-8H6v1h12zm-6 6.5a1 1 0 1 1 0-2a1 1 0 0 1 0 2"/>',
        "add-line": '<path fill="currentColor" d="M11 11V5h2v6h6v2h-6v6h-2v-6H5v-2z"/>',
        "refresh-line": '<path fill="currentColor" d="M5.463 4.433A9.96 9.96 0 0 1 12 2c5.523 0 10 4.477 10 10c0 2.136-.67 4.116-1.81 5.74L17 12h3A8 8 0 0 0 6.46 6.228zm13.074 15.134A9.96 9.96 0 0 1 12 22C6.477 22 2 17.523 2 12c0-2.136.67-4.116 1.81-5.74L7 12H4a8 8 0 0 0 13.54 5.772z"/>',
        "reply-line": '<path fill="currentColor" d="M11 20L1 12l10-8v5c5.523 0 10 4.477 10 10q0 .41-.032.81a9 9 0 0 0-7.655-4.805L13 15h-2zm-2-7h4.034l.347.007c1.285.043 2.524.31 3.676.766A7.98 7.98 0 0 0 11 11H9V8.161L4.202 12L9 15.839z"/>',
        "search-line": '<path fill="currentColor" d="m18.031 16.617l4.283 4.282l-1.415 1.415l-4.282-4.283A8.96 8.96 0 0 1 11 20c-4.968 0-9-4.032-9-9s4.032-9 9-9s9 4.032 9 9a8.96 8.96 0 0 1-1.969 5.617m-2.006-.742A6.98 6.98 0 0 0 18 11c0-3.867-3.133-7-7-7s-7 3.133-7 7s3.133 7 7 7a6.98 6.98 0 0 0 4.875-1.975z"/>',
        "share-2-line": '<path fill="currentColor" d="m12 2.586l6.207 6.207l-1.414 1.414L13 6.414V16h-2V6.414l-3.793 3.793l-1.414-1.414zM3 18v-4h2v4a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-4h2v4a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3"/>',
        "shield-cross-line": '<path fill="currentColor" d="M3.783 2.826L12 1l8.217 1.826a1 1 0 0 1 .783.976v9.987a6 6 0 0 1-2.672 4.992L12 23l-6.328-4.219A6 6 0 0 1 3 13.79V3.802a1 1 0 0 1 .783-.976M5 4.604v9.185a4 4 0 0 0 1.781 3.328L12 20.597l5.219-3.48A4 4 0 0 0 19 13.79V4.604L12 3.05zM11 10V7h2v3h3v2h-3v3h-2v-3H8v-2z"/>',
        "shield-check-line": '<path fill="currentColor" d="m12 1l8.217 1.826a1 1 0 0 1 .783.976v9.987a6 6 0 0 1-2.672 4.992L12 23l-6.328-4.219A6 6 0 0 1 3 13.79V3.802a1 1 0 0 1 .783-.976zm0 2.049L5 4.604v9.185a4 4 0 0 0 1.781 3.328L12 20.597l5.219-3.48A4 4 0 0 0 19 13.79V4.604zm4.452 5.173l1.415 1.414L11.503 16L7.26 11.757l1.414-1.414l2.828 2.828z"/>',
        "sun-line": '<path fill="currentColor" d="M12 18a6 6 0 1 1 0-12a6 6 0 0 1 0 12m0-2a4 4 0 1 0 0-8a4 4 0 0 0 0 8M11 1h2v3h-2zm0 19h2v3h-2zM3.515 4.929l1.414-1.414L7.05 5.636L5.636 7.05zM16.95 18.364l1.414-1.414l2.121 2.121l-1.414 1.414zm2.121-14.85l1.414 1.415l-2.121 2.121l-1.414-1.414zM5.636 16.95l1.414 1.414l-2.121 2.121l-1.414-1.414zM23 11v2h-3v-2zM4 11v2H1v-2z"/>',
        "delete-bin-line": '<path fill="currentColor" d="M17 6h5v2h-2v13a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V8H2V6h5V3a1 1 0 0 1 1-1h8a1 1 0 0 1 1 1zm1 2H6v12h12zm-9 3h2v6H9zm4 0h2v6h-2zM9 4v2h6V4z"/>',
        "error-warning-line": '<path fill="currentColor" d="M12 22C6.477 22 2 17.523 2 12S6.477 2 12 2s10 4.477 10 10s-4.477 10-10 10m0-2a8 8 0 1 0 0-16a8 8 0 0 0 0 16m-1-5h2v2h-2zm0-8h2v6h-2z"/>',
        "user-line": '<path fill="currentColor" d="M4 22a8 8 0 1 1 16 0h-2a6 6 0 0 0-12 0zm8-9c-3.315 0-6-2.685-6-6s2.685-6 6-6s6 2.685 6 6s-2.685 6-6 6m0-2c2.21 0 4-1.79 4-4s-1.79-4-4-4s-4 1.79-4 4s1.79 4 4 4"/>',
        "user-settings-line": '<path fill="currentColor" d="M12 14v2a6 6 0 0 0-6 6H4a8 8 0 0 1 8-8m0-1c-3.315 0-6-2.685-6-6s2.685-6 6-6s6 2.685 6 6s-2.685 6-6 6m0-2c2.21 0 4-1.79 4-4s-1.79-4-4-4s-4 1.79-4 4s1.79 4 4 4m2.595 7.811a3.5 3.5 0 0 1 0-1.622l-.992-.573l1-1.732l.992.573A3.5 3.5 0 0 1 17 14.645V13.5h2v1.145c.532.158 1.012.44 1.405.812l.992-.573l1 1.732l-.991.573a3.5 3.5 0 0 1 0 1.622l.991.573l-1 1.732l-.992-.573a3.5 3.5 0 0 1-1.405.812V22.5h-2v-1.145a3.5 3.5 0 0 1-1.405-.812l-.992.573l-1-1.732zM18 19.5a1.5 1.5 0 1 0 0-3a1.5 1.5 0 0 0 0 3"/>',
        "group-line": '<path fill="currentColor" d="M2 22a8 8 0 1 1 16 0h-2a6 6 0 0 0-12 0zm8-9c-3.315 0-6-2.685-6-6s2.685-6 6-6s6 2.685 6 6s-2.685 6-6 6m0-2c2.21 0 4-1.79 4-4s-1.79-4-4-4s-4 1.79-4 4s1.79 4 4 4m8.284 3.703A8 8 0 0 1 23 22h-2a6 6 0 0 0-3.537-5.473zm-.688-11.29A5.5 5.5 0 0 1 21 8.5a5.5 5.5 0 0 1-5 5.478v-2.013a3.5 3.5 0 0 0 1.041-6.609z"/>',
        "close-line": '<path fill="currentColor" d="m12 10.587l4.95-4.95l1.414 1.414l-4.95 4.95l4.95 4.95l-1.415 1.414l-4.95-4.95l-4.949 4.95l-1.414-1.415l4.95-4.95l-4.95-4.95L7.05 5.638z"/>'
    };

    const aliases = {
        "chevron-left": "arrow-left-s-line",
        "chevron-right": "arrow-right-s-line",
        "skip-back": "skip-back-line",
        "skip-forward": "skip-forward-line",
        "arrow-left": "arrow-left-line",
        "arrow-right": "arrow-right-line",
        "attachment": "attachment-2",
        "badge-dollar-sign": "money-dollar-circle-line",
        "bar-chart-3": "bar-chart-line",
        "bell": "notification-3-line",
        "bot-message-square": "robot-2-line",
        "calendar-range": "calendar-line",
        "check": "check-line",
        "chart-no-axes-combined": "bar-chart-box-line",
        "chart-spline": "line-chart-line",
        "chevron-down": "arrow-down-s-line",
        "chevron-up": "arrow-up-s-line",
        "contact-round": "contacts-line",
        "external-link": "external-link-line",
        "edit": "edit-line",
        "edit-3": "edit-line",
        "eye": "eye-line",
        "eye-off": "eye-off-line",
        "file": "file-line",
        "file-text": "file-text-line",
        "heart": "heart-line",
        "history": "history-line",
        "home": "home-smile-2-line",
        "image-up": "image-add-line",
        "kanban": "layout-grid-line",
        "laptop-minimal": "macbook-line",
        "list-checks": "list-check-3",
        "loader-circle": "loader-4-line",
        "lock": "lock-line",
        "log-out": "logout-box-r-line",
        "login-box": "login-box-line",
        "mail": "mail-line",
        "phone": "phone-line",
        "menu": "menu-line",
        "message-circle": "chat-3-line",
        "message-square-text": "chat-1-line",
        "messages-square": "mail-volume-line",
        "moon": "moon-clear-line",
        "mouse-pointer-click": "cursor-line",
        "panel-left-close": "sidebar-fold-line",
        "panel-left-open": "sidebar-unfold-line",
        "panel-right-close": "layout-right-line",
        "panel-right-open": "layout-left-line",
        "panels-top-left": "layout-grid-line",
        "pie-chart": "pie-chart-2-line",
        "pencil": "edit-line",
        "plug-zap": "plug-line",
        "plus": "add-line",
        "refresh-cw": "refresh-line",
        "reply": "reply-line",
        "search-check": "search-line",
        "share-2": "share-2-line",
        "shield-alert": "shield-cross-line",
        "shield-check": "shield-check-line",
        "shield": "shield-check-line",
        "shield-plus": "shield-cross-line",
        "key-round": "user-settings-line",
        "circle-check-big": "check-line",
        "crown": "shield-check-line",
        "info": "error-warning-line",
        "sun": "sun-line",
        "trash-2": "delete-bin-line",
        "triangle-alert": "error-warning-line",
        "user-round": "user-line",
        "user-plus": "group-line",
        "user-round-cog": "user-settings-line",
        "users": "group-line",
        "users-round": "group-line",
        "x": "close-line"
    };

    function createIcons(root = document) {
        const elements = [...root.querySelectorAll("i[data-lucide]")];
        if (root.matches?.("i[data-lucide]")) elements.unshift(root);
        elements.forEach(element => {
            const requested = element.getAttribute("data-lucide");
            const iconName = aliases[requested] || requested;
            const body = icons[iconName];
            if (!body) return;

            const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
            for (const attribute of element.attributes) svg.setAttribute(attribute.name, attribute.value);
            svg.setAttribute("viewBox", "0 0 24 24");
            svg.setAttribute("aria-hidden", "true");
            svg.setAttribute("focusable", "false");
            svg.setAttribute("data-remix-icon", iconName);
            svg.innerHTML = body;
            element.replaceWith(svg);
        });
    }

    window.lucide = { createIcons };

    // Las tablas y formularios se insertan después de cargar el módulo.
    // Convierte también los iconos de búsquedas, filtros y filas nuevas.
    const observer = new MutationObserver(records => {
        for (const record of records) {
            for (const node of record.addedNodes) {
                if (node.nodeType === Node.ELEMENT_NODE &&
                    (node.matches("i[data-lucide]") || node.querySelector("i[data-lucide]"))) {
                    createIcons(node);
                }
            }
        }
    });
    observer.observe(document.documentElement, { childList: true, subtree: true });
    createIcons();
})();
