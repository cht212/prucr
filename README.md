# CRM MySQL

El proyecto activo es `CRM.Data.MySql`. La solucion `CRM.Data.slnx`
incluye solamente la aplicacion MySQL y sus pruebas.

## Estructura

- `CRM.Data.MySql/`: aplicacion, migraciones, interfaz y documentacion.
- `CRM.Data.MySql/tests/CRM.Data.Tests/`: pruebas automatizadas.
- `CRM.Data.slnx`: solucion para Visual Studio y la CLI de .NET.

## Desarrollo

Requiere el SDK de .NET 10 y una base de datos MySQL configurada.
La configuracion local y las credenciales no se guardan en Git.
Consulta la documentacion dentro de `CRM.Data.MySql/docs` antes de
configurar las conexiones e integraciones.

```powershell
dotnet restore CRM.Data.slnx
dotnet build CRM.Data.slnx
dotnet test CRM.Data.slnx
dotnet run --project CRM.Data.MySql/CRM.Data.csproj
```

Los proyectos antiguos, diagnosticos locales, archivos temporales y
resultados de compilacion no forman parte del repositorio activo.
