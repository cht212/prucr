# Manual de uso del CRM HPD MySQL

Actualizado: 2 de octubre de 2026.

## Acceso y perfiles

Inicia sesion con usuario y contrasena. Las funciones disponibles dependen
del rol base, del rol personalizado y de los permisos individuales.

- **Administrador:** acceso completo; administra usuarios y conexiones.
- **Supervisor:** coordina la operacion y el equipo.
- **Asesor:** atiende los casos autorizados para su cuenta.
- **Auditor:** consulta la informacion que sus permisos permitan.
- **Marketing:** trabaja con los modulos y acciones comerciales autorizados.

Los permisos heredados de roles no administradores pueden concederse o retirarse.
No se presupone que todos los usuarios de un mismo rol tienen el mismo acceso.

## Comunicaciones

La bandeja separa Todas, Nuevas, Abiertas, Mis chats y Pendientes.
El selector **Sin filtro** esta junto a Pendientes.

**Ver todos los chats** permite consultar conversaciones de otros asesores y
cerradas dentro de los canales autorizados. No permite por si solo responder,
reasignar ni controlar el bot de un chat ajeno. Sin ese permiso se muestran
los chats propios y los disponibles para tomar, segun las reglas de acceso.

WhatsApp, Facebook e Instagram son canales distintos. Una cuenta sin chats
WhatsApp puede tener conversaciones en los otros canales. Sus accesos se
configuran por separado.

La conversacion carga los mensajes recientes y permite recuperar el historial
anterior. Los reintentos del mismo envio de texto reutilizan su clave para
evitar guardar dos veces el mensaje.

## Contactos y ficha

Contactos permite buscar por nombre, telefono, email, canal y asesor.
Cuando **Ficha completa del cliente** esta habilitada, tambien aparecen
la columna y el filtro de etiquetas, y la busqueda puede encontrarlas.

Sin ese permiso, las etiquetas no se muestran en la tabla ni en el detalle
movil y no pueden consultarse mediante el filtro. El permiso **Ficha de datos
del contacto** no concede acceso a las etiquetas ni a la ficha completa.

Crear y editar contactos son acciones con permisos independientes.
La ficha completa reune notas, tareas, oportunidades y actividad autorizadas.

## Listas y paginacion

En las listas paginadas, **Elementos por pagina** permite elegir 10, 25, 50 o 100.
Las flechas llevan a la primera, anterior, siguiente y ultima pagina.
El tamano elegido se conserva por lista en ese navegador.

Cambiar un filtro o el tamano de pagina reinicia la lista en la primera pagina.
El rango del pie indica cuantos registros se muestran sobre el total.

## Dashboard, tareas y ventas

Dashboard muestra indicadores y carga operativa segun el acceso del usuario.
Tareas permite organizar seguimientos y vencimientos. Ventas registra etapas,
montos, responsables y cierres de oportunidades. Las acciones de gestion y
exportacion requieren sus propios permisos.

## Marketing

El apartado abre antes de que terminen las consultas de redes. Sus bloques
muestran estados de carga y errores recuperables cuando el proveedor demora
o falla. Los filtros de canal y fechas determinan las metricas solicitadas.

TikTok se utiliza para publicaciones y metricas disponibles; no se ofrece
integracion de mensajes directos con TikTok.

## Usuarios y permisos

En **Usuarios**, el administrador crea cuentas, asigna roles base o
personalizados y configura accesos. En **Permisos > Comunicaciones** se
controlan Ver todos los chats, la ficha completa, los datos del contacto,
los canales y las acciones de atencion.

Los cambios se aplican al refrescar los permisos de la sesion y se validan
tambien en el servidor. La cuenta administradora mantiene acceso completo.

Las contrasenas se almacenan como hashes y no se pueden recuperar. El
administrador puede establecer una nueva; ninguna guia contiene claves reales.

## Conexiones y archivos

El administrador configura R2 y las redes desde **Conexiones**.
Los secretos se guardan protegidos en el servidor y se muestran enmascarados.
Las cuentas vinculadas deben tener los permisos aprobados por cada proveedor.

Los adjuntos de WhatsApp admiten los tipos y limites validados por el CRM,
incluido el limite de 15 MB en la subida actual. Los adjuntos locales se
descargan mediante rutas autenticadas; no necesitan un directorio publico.

Para cerrar la sesion, usa el menu del usuario y **Cerrar sesion**.

