# Arquitectura y mantenimiento del CRM MySQL

Actualizado: 2 de octubre de 2026.

## Proyecto activo

`CRM.Data.MySql` es una aplicacion ASP.NET Core .NET 10 con interfaz
HTML/CSS/JavaScript, API y Entity Framework Core. Usa MySQL 8.
La solucion de la raiz incluye la aplicacion y sus pruebas; el proyecto
anterior SQL Server no forma parte de la instalacion activa.

| Carpeta | Responsabilidad |
|---|---|
| `Controllers` | API y autorizacion de operaciones |
| `Data` y `Models` | Mapeo y entidades de MySQL |
| `Migrations` | Historial de EF y snapshot del esquema |
| `Services` | Negocio, canales, permisos y almacenamiento |
| `Extensions` | Servicios, autenticacion y seguridad HTTP |
| `Startup` | Migraciones y bootstrap del administrador |
| `wwwroot` | Interfaz, estilos, fuentes e imagenes |
| `tests/CRM.Data.Tests` | Pruebas automatizadas |
| `docs` | Las cuatro guias operativas y el SQL de instalacion |

## Base de datos

`DatabaseInitializer` aplica las migraciones al arrancar. El historial termina
en `20261002213909_AddMessageClientRequestId`.
El SQL oficial esta en [smarterasp/crear-base-datos.sql](smarterasp/crear-base-datos.sql);
no se mantiene una segunda copia en `Migrations`.

Incluye los permisos individuales, roles personalizados, contexto de respuestas,
indices compuestos para las listas y `crm_mensaje.c_client_request_id`.
El indice unico por conversacion y clave permite reutilizar un envio de texto
guardado cuando el cliente lo reintenta. No se exige una clave a los mensajes
anteriores: la columna admite NULL.

El SQL crea un esquema vacio, no migra datos locales. No contiene administradores
prefijados ni claves. Consulta [MySQL y migraciones](MYSQL-MIGRATION.md).

## Autenticacion y permisos

La sesion usa cookie HTTP-only. Fuera de desarrollo se usan cookies Secure,
HTTPS y HSTS. Hay limites de solicitudes, control de origen en escrituras
y bloqueo persistente de intentos fallidos.

`CrmPermissionService` combina rol base, rol personalizado y permisos
individuales. Los controles del servidor son obligatorios; ocultar botones
en JavaScript no reemplaza la autorizacion.

- `comunicaciones.chats.todos`: lectura de chats ajenos y cerrados en los
  canales permitidos; no otorga acciones de gestion.
- `comunicaciones.ficha`: ficha completa y lectura de etiquetas.
- `comunicaciones.ficha.contacto`: datos basicos del contacto, no etiquetas.
- Los permisos de WhatsApp, Facebook e Instagram son independientes.
- El administrador conserva acceso completo.

Sin ficha completa, Contactos devuelve las etiquetas vacias, no busca por sus
nombres y rechaza el filtro por etiqueta, incluida la exportacion.
Los endpoints de lectura de etiquetas tambien requieren ficha completa.

## Interfaz y consultas

Comunicaciones, Contactos, Tareas, Leads, Ventas, Actividad, Fallos y Usuarios
usan paginacion. Se pueden elegir 10, 25, 50 o 100 elementos, con navegacion
a los extremos y preferencia por lista en localStorage.

El historial de mensajes usa un cursor para recuperar bloques anteriores.
El cambio rapido de conversacion descarta respuestas de selecciones obsoletas.

Marketing y Conexiones cargan sus bloques progresivamente. Las peticiones
externas tienen limites de espera y presentan errores recuperables.
Dashboard separa el resumen de la consulta de carga del equipo.

Los estilos estan divididos en `wwwroot/css`: base, comunicaciones, modulos,
dashboard, marketing, ajustes responsivos y tema de referencia.
Mantener las nuevas reglas dentro del modulo correspondiente evita conflictos.
Los archivos estaticos usan cache de siete dias; al cambiarlos se actualiza
su version en `wwwroot/index.html`. HTML se entrega sin cache persistente.

## Almacenamiento y secretos

La configuracion local vive en `configuracion-local.env`; IIS usa
`ConnectionStrings__DefaultConnection` y variables protegidas del pool.
No publiques el archivo local: su cargador puede reemplazar la conexion de IIS.

Las conexiones sociales configuradas en el CRM se guardan en
`App_Data/social-integrations.json`. Las llaves de Data Protection persisten
en `App_Data/data-protection-keys`; en Windows se protegen mediante DPAPI
con alcance de maquina. El bot y las plantillas tambien usan archivos locales.

R2 almacena adjuntos cuando esta configurado. El almacenamiento de respaldo
usa `App_Data/private-uploads` y rutas autenticadas. El directorio publico
`/uploads` esta bloqueado.

La publicacion excluye App_Data local, appsettings locales, documentos,
pruebas y resultados temporales. En el servidor, respalda MySQL y App_Data,
y conserva sus archivos al actualizar. Las llaves cifradas en otra maquina
pueden no ser reutilizables: vuelve a configurar las conexiones en el hosting.

## Integraciones y salud

- `GET /api/health/live`: vida del proceso.
- `GET /api/health/ready`: prueba de conexion a MySQL; no valida todo el esquema.
- `/api/whatsapp/webhook`: WhatsApp.
- `/api/integraciones/meta/webhook`: Facebook e Instagram.
- TikTok: publicaciones y metricas, no mensajes directos.

Las rutas OAuth y webhooks deben usar el dominio HTTPS definitivo y los
valores generados por el apartado Conexiones. No incluyas tokens en la guia.

## Verificacion y despliegue

```powershell
dotnet restore .\CRM.Data.csproj
dotnet test .\tests\CRM.Data.Tests\CRM.Data.Tests.csproj -c Release
Get-ChildItem .\wwwroot\js -Filter *.js | ForEach-Object { node --check $_.FullName }
dotnet publish .\CRM.Data.csproj -p:PublishProfile=SmarterASP
```

Si el proceso Debug esta abierto, las comprobaciones pueden usar
`--configuration Audit` para no interferir con su ejecutable.

La prueba `DeploymentSqlTests` compara el SQL publicado con las migraciones
de EF sin conectarse a una base real. Ademas, cualquier cambio de esquema debe
probarse en una base aislada antes de ejecutarse sobre datos de produccion.

Guia de publicacion: [SmarterASP.NET](SMARTERASP_DESPLIEGUE.md).

