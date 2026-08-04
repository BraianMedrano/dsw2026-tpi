# Verificación del desarrollo

Ejecutar estos comandos desde la raíz del repositorio:

```powershell
dotnet restore Dsw2026Tpi.slnx
dotnet build Dsw2026Tpi.slnx --no-restore
dotnet test Dsw2026Tpi.slnx --no-build
```

Los comandos separan intencionalmente la restauración de dependencias, la compilación y la ejecución de pruebas. De este modo, el desarrollo local y la integración continua pueden utilizar los mismos límites para identificar fallos.

## Base de datos local

El proyecto utiliza SQLite para que todos los integrantes trabajen con el mismo proveedor de base de datos en Windows ARM64, x64 y x86, macOS ARM64 y x64, y Linux, sin instalar SQL Server ni LocalDB.

Los contextos `Dsw2026TpiDbContext` y `AuthenticationDbContext` comparten `Dsw2026Tpi.Api/Dsw2026Tpi.db`, pero cada uno conserva su propio modelo y sus propias migraciones. Por eso siempre deben aplicarse en este orden:

1. **Domain:** `Dsw2026TpiDbContext`.
2. **Identity:** `AuthenticationDbContext`.

Las migraciones actuales `AlignDoctorSoftDeleteContract` y `AddRevokedTokens` pertenecen a contextos distintos. Aplicar solamente uno deja incompleto el contrato de Doctor o la revocación de JWT.

### Actualizar una base local existente

1. Detener la API para evitar bloqueos de SQLite.
2. Si los datos locales son útiles, hacer una copia opcional:

```powershell
Copy-Item -LiteralPath .\Dsw2026Tpi.Api\Dsw2026Tpi.db -Destination .\Dsw2026Tpi.Api\Dsw2026Tpi.backup.db
```

3. Aplicar primero Domain y después Identity desde la raíz del repositorio:

```powershell
dotnet ef database update --context Dsw2026TpiDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
dotnet ef database update --context AuthenticationDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
```

Los comandos conservan los datos existentes y aplican únicamente las migraciones pendientes. Si el archivo todavía no existe, crean `Dsw2026Tpi.Api/Dsw2026Tpi.db`.

### Comprobar el estado de las migraciones

Estos comandos se conectan a la base configurada y muestran las migraciones de cada contexto; EF Core identifica las que todavía están pendientes:

```powershell
dotnet ef migrations list --context Dsw2026TpiDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
dotnet ef migrations list --context AuthenticationDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
```

Para comprobar si el modelo cambió sin crear su migración correspondiente:

```powershell
dotnet ef migrations has-pending-model-changes --context Dsw2026TpiDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
dotnet ef migrations has-pending-model-changes --context AuthenticationDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
```

La primera comprobación compara la base con las migraciones existentes; la segunda compara el modelo de código con el último snapshot. Son verificaciones diferentes.

### Reconstruir una base local descartable

Este procedimiento elimina todos los datos. Usarlo únicamente con una base local descartable: **nunca eliminar una base compartida, de pruebas integradas o de producción**.

1. Detener la API.
2. Eliminar la base local y los archivos auxiliares de SQLite:

```powershell
Remove-Item -LiteralPath .\Dsw2026Tpi.Api\Dsw2026Tpi.db, .\Dsw2026Tpi.Api\Dsw2026Tpi.db-wal, .\Dsw2026Tpi.Api\Dsw2026Tpi.db-shm -Force -ErrorAction SilentlyContinue
```

3. Volver a ejecutar los dos comandos de `database update`, en el orden Domain → Identity. Como alternativa, iniciar la API en el ambiente `Development`.

La base de datos, su copia de respaldo y los archivos auxiliares temporales están ignorados por Git.

### Migraciones según el ambiente

En `Development`, `ApplicationStartupInitializer` aplica automáticamente primero Domain y después Identity al iniciar la API. En cualquier otro ambiente, las migraciones deben ejecutarse explícitamente como parte del despliegue; la API no modifica el esquema automáticamente.

## Flujo equivalente en Visual Studio

1. Seleccionar `Dsw2026Tpi.Api` como proyecto de inicio.
2. Usar **Build > Build Solution** para compilar.
3. Abrir **Test > Test Explorer** y ejecutar todos los tests o el grupo `Authentication`.
4. Iniciar con el perfil HTTPS para que Development aplique las migraciones existentes y abra Swagger.

La interfaz de Visual Studio y los comandos anteriores ejecutan el mismo build y los mismos tests. Los comandos quedan documentados para CI y para diagnosticar fallos, pero no son obligatorios para el trabajo cotidiano.
