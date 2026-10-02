# Manual de uso del CRM HPD MySQL

Fecha de actualización: 29 de septiembre de 2026.

## 1. Acceso

Abre la dirección del CRM e introduce usuario y contraseña. La sesión se cierra
desde el menú de usuario.

Los perfiles son:

- **Administrador:** controla el sistema, usuarios, permisos y conexiones.
- **Auditor:** consulta la operación completa y los controles técnicos; solo
  modifica aquello que el Administrador le conceda expresamente.
- **Supervisor:** gestiona la operación y el trabajo del equipo.
- **Asesor:** trabaja con registros asignados y nuevos casos disponibles.

## 2. Dashboard

Muestra clientes, conversaciones, tareas, oportunidades, ventas y canales. El
contenido se adapta al rol y se actualiza al entrar o recargar el módulo.

## 3. Clientes

Permite buscar, crear y actualizar personas. La ficha reúne nombre, teléfono,
correo, documento, origen, foto, etiquetas, conversaciones, tareas, notas y
oportunidades.

## 4. Bandeja de conversaciones

Desde la bandeja se puede leer y responder mensajes, enviar archivos, añadir
notas, asignar responsable, cambiar estado, controlar el bot y crear tareas u
oportunidades. Los asesores pueden editar sus contactos, usar todas las etapas
comerciales, pausar el bot de sus conversaciones y solicitar una transferencia;
el supervisor puede reasignar directamente desde el chat. WhatsApp muestra el
número receptor cuando el WABA usa varios números.

## 5. Tareas, oportunidades y reportes

Las tareas registran título, fecha límite, prioridad, responsable y relación con
el cliente. Las oportunidades registran etapa, monto, probabilidad, responsable
y cierre esperado.

Reportes permite filtrar por fechas y asesor, revisar la carga operativa,
exportar CSV y consultar publicaciones e interacciones de Facebook, Instagram y
TikTok según los permisos de cada API.

## 6. Conexiones

Conexiones muestra el estado de WhatsApp, Facebook, Instagram, TikTok y
Cloudflare R2. Cada red presenta solo su configuración y su acción operativa
principal. TikTok se limita a publicaciones y métricas; no se presenta como
canal de mensajes directos. Los secretos aparecen enmascarados y solo el
Administrador puede revelarlos o administrarlos.

WhatsApp puede usar un número o una lista JSON de números del mismo WABA:

```json
[{"phoneNumberId":"123456789012345","displayNumber":"+51 999 999 999"}]
```

## 7. Usuarios y contraseñas

El Administrador abre **Usuarios** para crear una cuenta, elegir o cambiar su
rol entre Asesor, Supervisor y Auditor, cambiar su contraseña y administrar
permisos adicionales. Las contraseñas no se pueden leer
después de guardarlas porque MySQL conserva únicamente hashes seguros.

El botón **Permisos** de cada usuario permite marcar accesos adicionales. Los
incluidos por el rol aparecen seleccionados y no se pueden retirar desde ese
panel. Los cambios de visibilidad se reflejan cuando el usuario vuelve a iniciar
sesión.

La cuenta administradora inicial se configura localmente en
`configuracion-local.env`. Esos valores solo sirven para crear la primera
cuenta cuando la base aún no tiene usuarios. Para cambiar una cuenta existente,
se usa el módulo **Usuarios**.

## 8. Archivos

Los archivos aceptados incluyen PDF, Word, Excel, JPG, JPEG, PNG y WEBP, hasta
15 MB en el flujo de adjuntos de WhatsApp. Los documentos, imágenes, audios,
videos y stickers recibidos se muestran mediante una ruta autenticada del CRM;
el bucket R2 no necesita ser público.

## 9. Cierre de sesión

Usa el menú de usuario y selecciona **Cerrar sesión**. La aplicación elimina la
cookie de autenticación del navegador.

