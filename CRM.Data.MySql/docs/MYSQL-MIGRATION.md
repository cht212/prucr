# MySQL local, Docker y migraciones

## Configuración local

Todas las credenciales locales se guardan en `configuracion-local.env`. Editar
estas variables:

```env
MYSQL_HOST=localhost
MYSQL_PORT=3306
MYSQL_DATABASE=crm_hdp
MYSQL_USER=crm_user
MYSQL_PASSWORD=CLAVE_USUARIO
MYSQL_ROOT_PASSWORD=CLAVE_ROOT
CRM_BOOTSTRAP_USERNAME=admin
CRM_BOOTSTRAP_PASSWORD=CLAVE_ADMIN_CRM
```

No se debe copiar la conexión a `appsettings.json`. El archivo local contiene
secretos y está cubierto por `.gitignore`.

## Operación de Docker

```powershell
.\docker-local.cmd up
.\docker-local.cmd status
.\docker-local.cmd logs
.\docker-local.cmd stop
.\docker-local.cmd start
.\docker-local.cmd restart
```

`up` crea el volumen externo `crm_data_mysql` si todavía no existe. Detener o
recrear el contenedor no borra los datos.

## Cambiar la contraseña MySQL

No basta con editar `MYSQL_PASSWORD` después de crear la base: MySQL conserva la
clave anterior dentro de su volumen. Para mantener ambas partes sincronizadas:

```powershell
.\rotar-clave-mysql.cmd
```

El script pide confirmación, genera una clave, ejecuta `ALTER USER`, actualiza
`configuracion-local.env` y recrea el contenedor. No cambia
`MYSQL_ROOT_PASSWORD`, porque esa cuenta no debe usarla la aplicación.

## Migraciones

El esquema se aplica automáticamente al arrancar si MySQL está disponible.
También puede actualizarse manualmente:

```powershell
dotnet ef migrations list --project .\CRM.Data.csproj
dotnet ef database update --project .\CRM.Data.csproj
```

Las migraciones de SQL Server no se reutilizan. La carpeta `Migrations` de este
proyecto fue creada para MySQL y utiliza `utf8mb4`.

## Administrador inicial

El bootstrap solo actúa si no existen usuarios. Después del primer arranque, el
Administrador cambia contraseñas desde el módulo **Usuarios**. Modificar el
archivo local no reemplaza la contraseña de una cuenta existente.


