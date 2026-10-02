# MySQL local y migraciones

Actualizado: 2 de octubre de 2026. Ejecuta estos comandos desde `CRM.Data.MySql`.

## Configuracion local

Crea o edita `configuracion-local.env` con valores propios:

```env
MYSQL_HOST=localhost
MYSQL_PORT=3306
MYSQL_DATABASE=crm_hdp
MYSQL_USER=crm_user
MYSQL_PASSWORD=TU_CLAVE_MYSQL
MYSQL_ROOT_PASSWORD=TU_CLAVE_ROOT
CRM_BOOTSTRAP_USERNAME=TU_USUARIO_ADMIN
CRM_BOOTSTRAP_PASSWORD=TU_CLAVE_ADMIN
```

Los valores anteriores son marcadores, no contrasenas para reutilizar.
El archivo esta excluido de Git y de la publicacion. No lo copies al hosting:
el cargador local puede reemplazar la conexion definida en IIS.

## Docker

```powershell
.\docker-local.cmd up
.\docker-local.cmd status
.\docker-local.cmd logs
.\docker-local.cmd stop
.\docker-local.cmd start
.\docker-local.cmd restart
```

Docker usa MySQL 8.0.39 y el volumen externo `crm_data_mysql`.
Detener o recrear el contenedor conserva la base; no elimines ese volumen.

Para cambiar la clave del usuario MySQL sin perder los datos:

```powershell
.\rotar-clave-mysql.cmd
```

Este comando cambia la clave en MySQL y actualiza el archivo local.
Editar solamente `MYSQL_PASSWORD` no cambia una cuenta ya creada.

## Historial vigente

| Migracion | Cambio |
|---|---|
| `20260929154259_InitialMySql` | Esquema inicial MySQL |
| `20260929201516_AddUserPermissions` | Permisos individuales |
| `20260930230000_AddMessageReplyContext` | Contexto de respuesta |
| `20261002142642_AddPerformanceIndexes` | Indices y columnas indexables |
| `20261002181944_AddCustomRoles` | Roles personalizados |
| `20261002213909_AddMessageClientRequestId` | Evita duplicados al reintentar mensajes |

El CRM ejecuta `Database.MigrateAsync()` al arrancar. Si falla, registra el
error; un endpoint de vida saludable no demuestra que las tablas esten completas.
Verifica el historial de migraciones y el login.

```powershell
dotnet ef migrations list --project .\CRM.Data.csproj
dotnet ef database update --project .\CRM.Data.csproj
```

Antes de actualizar una base con datos, crea un respaldo. No ejecutes el SQL
de instalacion nueva sobre esa base y no borres el historial de EF.

## SQL para una base nueva

El unico archivo de instalacion es
[smarterasp/crear-base-datos.sql](smarterasp/crear-base-datos.sql).
Usa tablas InnoDB y `utf8mb4`, e incluye `__EFMigrationsHistory`.

No es un volcado de datos ni un conversor de SQL Server. Ejecutalo una sola vez
en una base MySQL 8 vacia. MySQL confirma las operaciones DDL implicitamente:
si falla a mitad, no repitas el archivo sobre las tablas parciales.

Cuando se agregue una migracion, regenera el SQL:

```powershell
dotnet ef migrations script 0 --project .\CRM.Data.csproj -c Release --output .\docs\smarterasp\crear-base-datos.sql
```

La generacion de EF no conserva el encabezado ni las opciones explicitas
InnoDB/`utf8mb4`: vuelve a aplicarlas y ejecuta las pruebas de
`DeploymentSqlTests` antes de publicar el archivo.

## Administrador inicial

El bootstrap crea una cuenta solo si `crm_usuario` esta vacia. Cambia su
contrasena desde **Usuarios** despues del primer ingreso. Modificar el archivo
local no cambia la contrasena de una cuenta existente.


