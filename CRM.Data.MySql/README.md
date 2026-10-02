# CRM Data MySQL

Esta carpeta contiene la aplicación CRM HPD configurada para MySQL, junto con
los recursos necesarios para ejecutarla localmente.

## Inicio rápido local

Requisitos: .NET 10 SDK, Docker Desktop y PowerShell.

1. Editar en `configuracion-local.env` las claves de MySQL y del administrador
   inicial. Si se utiliza el almacenamiento R2 ya integrado, completar también
   sus variables.
2. Iniciar MySQL:

   ```powershell
   .\docker-local.cmd up
   ```

3. Ejecutar el CRM:

   ```powershell
   dotnet run --project .\CRM.Data.csproj
   ```

4. Abrir la URL que muestre la consola. Los perfiles de Visual Studio usan
   `https://localhost:7081` y `http://localhost:5081`.

## Configuración sencilla

- Usuario y clave inicial del CRM: `CRM_BOOTSTRAP_USERNAME` y
  `CRM_BOOTSTRAP_PASSWORD` en `configuracion-local.env`.
- Usuario y claves de MySQL: `MYSQL_USER`, `MYSQL_PASSWORD` y
  `MYSQL_ROOT_PASSWORD` en el mismo archivo.
- R2: variables que comienzan por `R2_`.

El usuario bootstrap solo se crea cuando no hay ningún usuario en la base. Una
vez creado, su contraseña se cambia desde **Usuarios** dentro del CRM. Editar
`CRM_BOOTSTRAP_PASSWORD` no modifica una cuenta ya existente.

Para rotar de forma segura la clave del usuario MySQL local:

```powershell
.\rotar-clave-mysql.cmd
```

`configuracion-local.env` está ignorado por Git porque contiene secretos y debe
permanecer solamente en el entorno local.

## Documentación

Toda la documentación se encuentra en [`docs`](docs/INDICE.md):

- manual de uso;
- documentación técnica;
- Docker y migraciones MySQL;
- despliegue en SmarterASP.NET.

