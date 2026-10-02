# Despliegue del CRM en SmarterASP.NET

Esta guía prepara una instalación nueva. No copies `configuracion-local.env`,
`App_Data` ni `wwwroot/uploads` al servidor.

## 1. Crear la base MySQL

1. Entra al **Hosting Control Panel**.
2. Abre **Databases > MySQL**.
3. Pulsa **+ Database**.
4. Selecciona MySQL 8, escribe el nombre, crea una contraseña fuerte y asigna
   la cuota.
5. Guarda por separado estos valores: servidor, puerto, base, usuario y clave.

Hay dos formas válidas de crear las tablas:

- Recomendada: deja la base vacía. Al arrancar por primera vez, el CRM ejecuta
  automáticamente todas las migraciones.
- Por archivo: usa **Restore** sobre la base creada y carga
  `docs/smarterasp/crear-base-datos.sql`. Este archivo sirve para una base
  nueva y vacía; no debe ejecutarse dos veces.

## 2. Configurar la cadena de conexión

La conexión MySQL no puede configurarse dentro del CRM: es necesaria antes de
que existan el login y el panel. Debe vivir como variable protegida de IIS.

En **Advanced Tools > Pool Manager > Actions > Environment Variables**, crea:

| Nombre | Valor |
|---|---|
| `ConnectionStrings__DefaultConnection` | La cadena MySQL entregada por SmarterASP.NET |
| `Authentication__BootstrapUsername` | Usuario administrador inicial |
| `Authentication__BootstrapPassword` | Contraseña inicial larga y única |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

Las variables pertenecen al Application Pool. Si el plan lo permite, usa un
pool dedicado para que otros sitios de la misma cuenta no compartan la cadena
de conexión.

Ejemplo de estructura, solamente como referencia:

```text
Server=SERVIDOR;Port=3306;Database=BASE;User ID=USUARIO;Password=CLAVE;SslMode=Preferred;
```

Usa los nombres y opciones exactos que muestra el panel de SmarterASP.NET. No
guardes la cadena real en Git, `appsettings.json`, `web.config` ni este archivo.

Después del primer ingreso, cambia la contraseña desde **Usuarios** y elimina
las dos variables `Authentication__Bootstrap...` del hosting. No son necesarias
cuando la base ya contiene usuarios.

## 3. Publicar

Desde la carpeta del proyecto:

```powershell
dotnet publish CRM.Data.csproj -p:PublishProfile=SmarterASP
```

El resultado queda en `bin/SmarterASP`. Se puede subir mediante Web Deploy o
comprimir su contenido y cargarlo en la raíz del sitio. El perfil publica en
modo framework-dependent para .NET 10 y excluye datos locales, adjuntos y
credenciales.

No borres en futuras publicaciones estas carpetas del servidor:

- `App_Data/data-protection-keys`: permite descifrar las claves del panel.
- `App_Data/social-integrations.json`: contiene la configuración cifrada.
- `App_Data/private-uploads`: contiene adjuntos que todavía no estén en R2.

La identidad del Application Pool necesita permiso de escritura sobre
`App_Data`. Conviene incluir toda esa carpeta en los respaldos del hosting.
Las llaves están protegidas por Windows; si el proveedor mueve el sitio a otro
servidor y ya no pueden descifrarse, vuelve a ingresar las conexiones desde el
panel para generar un juego nuevo.

## 4. Configurar las conexiones desde el CRM

1. Abre la URL temporal de SmarterASP.NET e inicia sesión como administrador.
2. Entra a **Conexiones**.
3. Configura Cloudflare R2, WhatsApp, Instagram, Facebook o TikTok.
4. Las claves se guardan cifradas en el servidor. El formulario las muestra
   enmascaradas y únicamente el administrador puede solicitar verlas.
5. Para Instagram, Facebook o TikTok, guarda primero el App ID/Client Key y su
   secreto; después usa **Conectar con OAuth**. El CRM valida `state`, canjea el
   código en el servidor y guarda los tokens cifrados. El token de TikTok se
   renueva automáticamente antes de vencer.

Las claves ya no forman parte del paquete publicado. R2 también se administra
desde **Conexiones > Configurar almacenamiento**.

Registra en cada proveedor la URL exacta correspondiente al dominio definitivo:

```text
https://crm.ejemplo.com/api/integraciones/instagram/oauth/callback
https://crm.ejemplo.com/api/integraciones/facebook/oauth/callback
https://crm.ejemplo.com/api/integraciones/tiktok/oauth/callback
```

No agregues parámetros ni cambies la barra final respecto de la URL registrada.
TikTok exige HTTPS para el flujo web.

## 5. Agregar el dominio cuando esté decidido

1. En SmarterASP.NET abre **Websites** y usa **Add Domain Name** sobre el sitio.
2. En el proveedor del dominio elige una opción:
   - cambiar los DNS a `NS1.SITE4NOW.NET`, `NS2.SITE4NOW.NET` y
     `NS3.SITE4NOW.NET`; o
   - mantener el DNS actual y crear un registro `A` hacia la IP indicada por
     SmarterASP.NET.
3. Activa el certificado SSL para el dominio.
4. Si todo el frontend y la API usan el mismo dominio, no hace falta configurar
   CORS. Si otro sitio web consumirá la API, agrega en el Pool Manager:

```text
Cors__AllowedOrigins__0=https://crm.ejemplo.com
```

5. En **Conexiones**, copia nuevamente las URLs de webhook generadas por el CRM
   y actualízalas en Meta/TikTok. Siempre deben empezar por `https://`.

## 6. Verificación posterior

Comprueba en este orden:

1. `/api/health/live` devuelve `Healthy`.
2. `/api/health/ready` devuelve `Healthy` y `database: Available`.
3. La URL raíz del sitio muestra directamente el login si no hay una sesión
   activa; con una sesión válida abre el CRM. El login funciona y obliga a usar HTTPS.
4. En **Conexiones**, las claves aparecen como configuradas.
5. Envía un mensaje de prueba y prueba la subida/descarga de un archivo.
6. Recicla el Application Pool y comprueba que las conexiones siguen activas.

Si aparece un error 500 al iniciar, activa temporalmente el log de stdout en
`web.config`, reproduce el error, descarga el log y vuelve a desactivarlo.

## 7. Rendimiento

La aplicación entrega CSS, JavaScript, JSON y SVG con Brotli/Gzip y conserva
los archivos estáticos en caché durante siete días. En una prueba local de 40
solicitudes calientes se obtuvieron estos promedios:

- `login.html`: 2.24 ms;
- `/api/health/live`: 3.78 ms;
- `/api/health/ready`, incluyendo MySQL: 6.01 ms.

Estas cifras validan el código, pero no representan la latencia del centro de
datos. Después de publicar conviene medir desde la ubicación real de los
usuarios. Los dos logotipos PNG suman aproximadamente 1.24 MB y son el siguiente
candidato de optimización (WebP/AVIF) si la primera carga móvil resulta lenta.
