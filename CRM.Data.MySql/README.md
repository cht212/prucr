# CRM HPD MySQL

Actualizado: 2 de octubre de 2026. Proyecto activo: `CRM.Data.MySql`.
Repositorio: [cht212/prucr](https://github.com/cht212/prucr).

## Desarrollo local

Requisitos: .NET 10 SDK, Docker Desktop y PowerShell. Para validar JavaScript,
tambien se necesita Node.js.

1. Configura `configuracion-local.env` con tus claves locales. La estructura
   y los comandos estan en [MySQL local](docs/MYSQL-MIGRATION.md).
2. Inicia MySQL con `.\docker-local.cmd up`.
3. Ejecuta `dotnet run --project .\CRM.Data.csproj`.
4. Abre la URL indicada en la consola. Los puertos configurados estan en
   `Properties/launchSettings.json`; no se presupone un puerto fijo.

Las conexiones sociales y R2 se administran desde **Conexiones**. No copies
claves reales a documentos, codigo ni archivos publicados.

## SmarterASP.NET

Para tu instalacion nueva, importa **una sola vez** el archivo
[crear-base-datos.sql](docs/smarterasp/crear-base-datos.sql) en una base MySQL 8
vacia. Incluye las seis migraciones actuales, los roles personalizados,
los permisos, los indices de rendimiento y la clave de reintentos de mensajes.
No contiene tus clientes, chats ni contrasenas.

Sigue [la guia de despliegue](docs/SMARTERASP_DESPLIEGUE.md) para configurar
IIS, publicar y crear el administrador inicial.

## Documentacion necesaria

- [Manual de uso](docs/MANUAL_USO_CRM_HPD.md).
- [Arquitectura y mantenimiento](docs/DOCUMENTACION_COMPLETA_CRM_HPD.md).
- [MySQL local y migraciones](docs/MYSQL-MIGRATION.md).
- [Despliegue en SmarterASP.NET](docs/SMARTERASP_DESPLIEGUE.md).

No se mantienen copias Word ni informes duplicados. Los documentos retirados
se conservan en un respaldo fuera del repositorio.

## Verificacion

```powershell
dotnet test .\tests\CRM.Data.Tests\CRM.Data.Tests.csproj -c Release
Get-ChildItem .\wwwroot\js -Filter *.js | ForEach-Object { node --check $_.FullName }
```

## Git

Desde esta carpeta o desde la raiz del repositorio:

```powershell
git add .
git commit -m "Describe tus cambios"
git push
```

`origin` apunta a `https://github.com/cht212/prucr.git`. Una confirmacion local
no equivale a una subida: comprueba que `git push` termine correctamente.

