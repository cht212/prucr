# Documentación técnica del CRM HPD MySQL

Fecha de actualización: 29 de septiembre de 2026.

## 1. Alcance

Esta guía describe la variante ubicada en `CRM.Data.MySql`. Es una aplicación
ASP.NET Core .NET 10 que sirve una interfaz HTML/CSS/JavaScript y una API en el
mismo proceso. Usa Entity Framework Core con MySQL 8. El almacenamiento de
adjuntos utiliza Cloudflare R2 cuando sus credenciales y bucket están completos
y, en caso contrario, almacenamiento local protegido.

## 2. Abrir únicamente este proyecto

En Visual Studio Code se abre la carpeta `CRM.Data.MySql`. En Visual Studio se
abre `CRM.Data.csproj` dentro de esa carpeta. Los comandos de esta documentación
suponen que la terminal está situada en `CRM.Data.MySql`.

Estructura principal:

- `Controllers`: endpoints HTTP de negocio.
- `Data`: contexto y mapeo de Entity Framework Core.
- `DTOs`: contratos de entrada de las integraciones.
- `Extensions`: autenticación, servicios y seguridad HTTP.
- `Migrations`: historial y modelo MySQL.
- `Models`: entidades del CRM.
- `Services`: lógica de negocio, canales, seguridad y almacenamiento.
- `Startup`: inicialización de la base y del administrador.
- `wwwroot`: interfaz web.
- `docs`: toda la documentación del proyecto.

## 3. Inicio local

Requisitos:

- .NET 10 SDK;
- Docker Desktop con Docker Compose;
- PowerShell o terminal de Windows.

Preparación:

```powershell
notepad .\configuracion-local.env
.\docker-local.cmd up
dotnet run --project .\CRM.Data.csproj
```

Comprobar el estado:

```powershell
.\docker-local.cmd status
```

La aplicación lee `configuracion-local.env` desde su directorio de contenido.
Ese archivo se utiliza para desarrollo local y está ignorado por Git.

## 4. MySQL y Entity Framework Core

`Program.cs` construye `ConnectionStrings:DefaultConnection` a partir de:

- `MYSQL_HOST`;
- `MYSQL_PORT`;
- `MYSQL_DATABASE`;
- `MYSQL_USER`;
- `MYSQL_PASSWORD`.

Docker Compose inicia MySQL 8.0.39 y conserva sus datos en el volumen externo
`crm_data_mysql`. El volumen no se elimina al detener o recrear el contenedor.

`DatabaseInitializer` comprueba la conexión y aplica migraciones pendientes al
arrancar. Si MySQL no responde, registra el error y permite que el proceso siga
activo; `/api/health/ready` indicará que el servicio aún no está listo.

Comandos manuales de EF Core:

```powershell
dotnet ef migrations list --project .\CRM.Data.csproj
dotnet ef database update --project .\CRM.Data.csproj
```

## 5. Administrador inicial y contraseñas

`CRM_BOOTSTRAP_USERNAME` y `CRM_BOOTSTRAP_PASSWORD` definen la primera cuenta
Administradora. Esta cuenta solo se crea cuando `crm_usuario` está vacía.

Las contraseñas de usuarios se guardan como hashes mediante el sistema de
contraseñas de ASP.NET Core. Por diseño, no se pueden recuperar ni mostrar. El
Administrador puede sustituirlas desde **Usuarios**.

Cambiar el valor bootstrap no cambia una cuenta ya creada. Para una base nueva,
se edita antes de arrancar. Fuera del entorno de desarrollo, el valor literal
`change-me-now` es rechazado por la aplicación.

La contraseña local del usuario MySQL se rota de forma coordinada con:

```powershell
.\rotar-clave-mysql.cmd
```

El script cambia la clave dentro de MySQL, actualiza el archivo local y recrea
el contenedor sin borrar los datos.

## 6. Autenticación, roles y permisos

La sesión utiliza una cookie HTTP-only. Fuera de desarrollo, la aplicación
activa HTTPS, HSTS y la marca `Secure`. Hay límites de solicitudes, validación de
origen para operaciones de escritura y bloqueo persistente de accesos fallidos.

Roles base:

- Administrador;
- Auditor;
- Supervisor;
- Asesor.

Los permisos adicionales se guardan en MySQL. Los controladores y servicios
validan la autorización en el servidor; la visibilidad del frontend es solo una
capa complementaria.

## 7. Funcionalidad del CRM

Los módulos cubren clientes, conversaciones, mensajes, contactos, notas,
etiquetas, tareas, oportunidades, campañas, automatizaciones, plantillas,
dashboard, actividad, reportes y exportaciones.

WhatsApp admite varios números del mismo WABA. El webhook conserva
`phone_number_id` en la conversación y las respuestas posteriores utilizan ese
mismo número.

Facebook e Instagram comparten el webhook Meta. TikTok se utiliza para consultar
publicaciones y métricas mediante Display API cuando existe un token con el
permiso necesario; no se presenta como canal de mensajes directos.

## 8. Archivos y Cloudflare R2

`R2StorageService` usa la API compatible con S3 de Cloudflare R2. Requiere:

- `R2:AccountId`;
- `R2:AccessKeyId`;
- `R2:SecretAccessKey`;
- `R2:BucketName`.

En local, esas claves se cargan desde variables con guion bajo en
`configuracion-local.env`. Los archivos entrantes pueden recurrir a
`App_Data/private-uploads` si R2 falla; la descarga local exige sesión y acceso
al mensaje correspondiente.

El bucket puede permanecer privado. El CRM entrega los archivos al usuario por
una ruta autenticada y sube a Meta el contenido mediante la API de medios de
WhatsApp. Las credenciales deben limitarse al bucket y a lectura/escritura de
objetos.

## 9. Configuración operativa

Las conexiones sociales configuradas desde la interfaz se persisten en
`App_Data/social-integrations.json`. Los ajustes del bot y las respuestas
rápidas también se guardan bajo `App_Data`. Por tanto, esos archivos forman
parte de los datos de esta instalación y deben conservarse junto con la base de
datos.

## 10. Salud y webhooks

- `GET /api/health/live`: vida del proceso.
- `GET /api/health/ready`: disponibilidad de MySQL.
- `/api/whatsapp/webhook`: WhatsApp Cloud API.
- `/api/integraciones/meta/webhook`: Facebook e Instagram.

Los webhooks configurados en las plataformas externas apuntan a estas rutas y
validan sus firmas o tokens de verificación.

## 11. Seguridad de configuración

- No versionar `configuracion-local.env`.
- No colocar claves reales en `appsettings.json`.
- Usar usuarios MySQL sin privilegios administrativos para la aplicación.
- Rotar credenciales si alguna apareció en Git, capturas o mensajes.
- Conservar de forma segura las credenciales y las copias de seguridad de
  MySQL.

## 12. Comprobaciones del proyecto

```powershell
dotnet restore .\CRM.Data.csproj
dotnet build .\CRM.Data.csproj -c Release
Get-ChildItem .\wwwroot\js\*.js | ForEach-Object { node --check $_.FullName }
```

Estas comprobaciones validan la restauración de dependencias, la compilación y
la sintaxis de los archivos JavaScript actuales.

