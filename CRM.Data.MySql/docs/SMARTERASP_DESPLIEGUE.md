# Despliegue del CRM en SmarterASP.NET

Actualizado: 2 de octubre de 2026. Procedimiento para una base **nueva y vacia**.
SmarterASP anuncia soporte de MySQL 8 y .NET 10; confirma que ambos esten
habilitados en el sitio y plan asignados antes de publicar.
[Compatibilidad del proveedor](https://www.smarterasp.net/mysql_8_hosting).

## 1. Crear la base MySQL 8

En el Hosting Control Panel, abre **Databases > MySQL > + Database**.
Selecciona MySQL 8, establece nombre, clave y cuota, y guarda servidor, puerto,
base, usuario y contrasena en un gestor seguro.
[Instrucciones del proveedor](https://www.smarterasp.net/support/kb/a2393/how-can-i-create-a-mysql-database-from-the-hosting-control-panel.aspx).

Selecciona la base del hosting antes de importar. No uses la base local ni
un archivo SQL Server: este proyecto utiliza MySQL.

## 2. Importar el SQL actualizado

Archivo unico: [smarterasp/crear-base-datos.sql](smarterasp/crear-base-datos.sql).

En **Databases > MySQL**, localiza esa base y usa **Restore** para cargar el
archivo. Espera que el trabajo termine correctamente.
[Restauracion MySQL del proveedor](https://www.smarterasp.net/support/kb/a2399/how-can-i-restore-mysql-database-to-your-server.aspx).

El archivo crea tablas InnoDB con `utf8mb4` y registra las seis migraciones
vigentes, incluida `20261002213909_AddMessageClientRequestId`.
No crea la base ni usuarios MySQL y no necesita privilegios de administrador
global. No contiene tus clientes, chats, adjuntos ni contrasenas.

**Ejecutalo una sola vez y solo sobre una base vacia.** No es un script de
actualizacion. MySQL confirma DDL implicitamente; si una instruccion falla,
no supongas que START TRANSACTION puede deshacer las tablas ya creadas.
Si solo hay tablas parciales de esta instalacion nueva, recrea una base vacia
desde el panel antes de reintentar. Nunca borres una base con datos reales.

Comprueba despues de importar:

```sql
SELECT MigrationId, ProductVersion
FROM __EFMigrationsHistory ORDER BY MigrationId;

SHOW TABLES;
SHOW COLUMNS FROM crm_mensaje LIKE 'c_client_request_id';
SHOW COLUMNS FROM crm_usuario LIKE 'n_rol';
```

Debe haber seis registros de migracion, las tablas `crm_rol` y
`crm_rol_permiso`, y las dos columnas anteriores. El arranque del CRM
reconocera ese historial y no repetira las migraciones ya importadas.

Alternativa: deja la base completamente vacia y permite que el CRM aplique
las migraciones al arrancar. Elige una opcion; no importes el SQL despues
de que el arranque ya haya creado las tablas.

## 3. Configurar IIS y el primer administrador

En **Advanced Tools > Pool Manager > Actions > Environment Variables**,
configura estas variables en el pool del sitio:
[Variables de entorno del proveedor](https://www.smarterasp.net/support/kb/a2437/how-to-set-environment-variable-for-your-account.aspx).

| Variable | Valor |
|---|---|
| `ConnectionStrings__DefaultConnection` | Conexion MySQL proporcionada por el hosting |
| `Authentication__BootstrapUsername` | Usuario administrador inicial |
| `Authentication__BootstrapPassword` | Clave larga y unica |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

Ejemplo estructural, sin credenciales reales:

```text
Server=SERVIDOR;Port=3306;Database=BASE;User ID=USUARIO;Password=CLAVE;SslMode=Preferred;
```

Usa las opciones SSL y datos que indique el proveedor. No publiques la cadena
real en Git, documentos ni el paquete del sitio. Las variables pertenecen
al pool; si hay otros sitios dentro del mismo pool, tambien podrian acceder
a ellas. Un pool dedicado separa esa configuracion.

El SQL no inserta un administrador. El primer arranque lo crea solamente si
`crm_usuario` esta vacia y las variables bootstrap estan completas.
Despues del primer ingreso, cambia la clave desde **Usuarios** y elimina
las variables bootstrap. No uses `change-me-now` en produccion.

## 4. Publicar la aplicacion

Desde `CRM.Data.MySql`:

```powershell
dotnet test .\tests\CRM.Data.Tests\CRM.Data.Tests.csproj -c Release
dotnet publish .\CRM.Data.csproj -p:PublishProfile=SmarterASP
```

Sube el **contenido** de `bin/SmarterASP` a la raiz del sitio, no la carpeta
del proyecto completa. El perfil usa .NET 10 framework-dependent; el servidor
debe tener el runtime y el modulo de ASP.NET Core correspondientes.

El paquete no debe contener `configuracion-local.env`, appsettings locales,
`App_Data` de tu equipo, `wwwroot/uploads`, pruebas ni documentacion.
El SQL se importa por separado en el panel de bases; no va en la raiz web.

No sobrescribas ni borres `App_Data` ya existente en el hosting cuando hagas
actualizaciones. La identidad del pool necesita escritura en esa carpeta.
Respalda especialmente las llaves de Data Protection, las conexiones cifradas,
los ajustes del bot y los adjuntos locales privados.

Las llaves de Windows se protegen con DPAPI de maquina. Las conexiones
cifradas localmente pueden no funcionar en otro servidor: configura las
integraciones de nuevo en el hosting, no copies tus archivos locales.

## 5. Dominio, HTTPS e integraciones

Configura el dominio y el certificado desde el panel del hosting con los
valores que muestre tu cuenta. Activa HTTPS antes de probar el login en
produccion; las cookies de sesion usan Secure.

Como administrador, abre **Conexiones** y configura WhatsApp, Facebook,
Instagram, TikTok y R2 cuando correspondan. Registra las URLs de webhook
y OAuth generadas para el dominio definitivo. Las rutas OAuth incluyen:

```text
https://crm.ejemplo.com/api/integraciones/instagram/oauth/callback
https://crm.ejemplo.com/api/integraciones/facebook/oauth/callback
https://crm.ejemplo.com/api/integraciones/tiktok/oauth/callback
```

No incluyas tokens en los documentos. Los permisos aprobados por cada proveedor
determinan que datos se pueden consultar. TikTok no ofrece mensajes directos
en esta integracion.

## 6. Verificacion y respaldo

1. `/api/health/live` debe indicar Healthy.
2. `/api/health/ready` debe indicar Healthy y database Available.
3. Comprueba los seis registros de EF y que el login funcione: el endpoint
   ready solo comprueba la conexion, no valida todas las tablas.
4. Crea un contacto y prueba las listas, etiquetas y permisos de ficha.
5. Prueba Ver todos los chats con un usuario no administrador y sus canales.
6. Comprueba que Marketing abre y muestra carga o un error recuperable
   cuando las consultas de redes no responden.
7. Si configuraste un canal, prueba envio, adjuntos y recepcion del webhook.
8. Recicla el pool y confirma que las conexiones siguen configuradas.
9. Mantiene respaldos de MySQL y de App_Data del servidor.

Si falla el arranque, consulta los logs del hosting. El stdout de ASP.NET Core
puede activarse temporalmente en `web.config`; desactivalo despues del
diagnostico y nunca publiques logs con secretos.
