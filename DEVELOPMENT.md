# Verificación del desarrollo

Ejecutar estos comandos desde la raíz del repositorio:

```powershell
dotnet restore Dsw2026Tpi.slnx
dotnet build Dsw2026Tpi.slnx --no-restore
dotnet test Dsw2026Tpi.slnx --no-build
```

Los comandos separan intencionalmente la restauración de dependencias, la compilación y la ejecución de pruebas. De este modo, el desarrollo local y la integración continua pueden utilizar los mismos límites para identificar fallos.

## Base de datos local

El proyecto utiliza SQLite para que todos los integrantes trabajen con el mismo proveedor de base de datos en Windows ARM64, x64 y x86, macOS ARM64 y x64, y Linux, sin instalar SQL Server ni LocalDB. Los dos contextos de EF Core comparten `Dsw2026Tpi.db`, pero cada uno conserva su propio modelo y sus propias migraciones.

Crear o actualizar la base de datos local desde la raíz del repositorio:

```powershell
dotnet ef database update --context Dsw2026TpiDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
dotnet ef database update --context AuthenticationDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
```

Estos comandos crean `Dsw2026Tpi.Api/Dsw2026Tpi.db`. La base de datos y sus archivos auxiliares temporales están ignorados por Git. Para reconstruirla desde cero, detener la API, eliminar esos archivos locales y volver a ejecutar ambos comandos de migración.
