# Trabajo Práctico Integrador
## Desarrollo de Software 2026

## Integrantes

- Braian Medrano — Legajo 57121
- Fernando Chumba — Legajo 53951
- Tobias Juarez Julio — Legajo 57424

## Orden de implementación del backend

1. `feature/foundation`
2. `chore/ci`
3. `feature/auth` y `feature/specialties` en paralelo
4. `feature/doctors`
5. `feature/availabilities`
6. `feature/appointments`
7. `feature/patient-history`
8. `chore/observability`

Las ramas se organizan por módulo funcional, no por capa técnica, para reducir dependencias entre integrantes.

## Base de datos compartida

El equipo utiliza **SQLite como único proveedor de EF Core**. Los contextos de dominio e Identity conservan modelos y migraciones separados, pero comparten la base local `Dsw2026Tpi.Api/Dsw2026Tpi.db`. No se deben agregar migraciones de SQL Server ni cambiar el proveedor en ramas individuales.

La restauración, compilación, ejecución de pruebas y administración de la base local se documentan en [DEVELOPMENT.md](DEVELOPMENT.md).

## Autenticación

El administrador inicial se crea durante el arranque a partir de la configuración; no existe un endpoint público para registrar administradores. Los pacientes ingresan con email y DNI y su cuenta se crea automáticamente en el primer acceso.

La configuración, los flujos, las decisiones de seguridad y la prueba desde Visual Studio y Swagger se explican en [AUTHENTICATION.md](AUTHENTICATION.md).

### Ejecución inmediata en desarrollo

`appsettings.Development.json` incluye una clave JWT y un administrador de prueba, válidos solo en el ambiente `Development`. Desde un clon nuevo, la API arranca sin configuración adicional:

```powershell
git clone -b development https://github.com/BraianMedrano/dsw2026-tpi.git
cd dsw2026-tpi
dotnet run --project Dsw2026Tpi.Api --launch-profile http
```

Al iniciar, la API crea la base SQLite `Dsw2026Tpi.Api/Dsw2026Tpi.db`, aplica las migraciones y crea el administrador de prueba. Swagger queda disponible en `http://localhost:5278/swagger`.

| Usuario       | Email              | Contraseña   |
| ------------- | ------------------ | ------------ |
| Administrador | `admin@system.com` | `Admin2026!` |

- La clave JWT debe tener al menos 32 caracteres. Si se cambia, los tokens emitidos antes dejan de ser válidos y hay que volver a iniciar sesión.
- `appsettings.json` no contiene estos valores. Fuera de `Development`, la clave y el administrador deben configurarse con variables de entorno.
- User Secrets reemplaza los valores del archivo. Si un integrante ya tiene configurados `Jwt:Key` o `InitialAdministrator`, la API usa los suyos.

### Agregar otro administrador

1. Detener la API.
2. Cambiar `InitialAdministrator:Email` y `InitialAdministrator:Password` en `appsettings.Development.json` o en User Secrets.
3. Volver a iniciar la API.

El arranque crea el nuevo administrador y conserva la base. Los administradores anteriores siguen activos.

Cambiar solo la contraseña de un email existente no la actualiza, porque el arranque no modifica una cuenta ya creada. Para aplicarla en una base descartable, detener la API, eliminar `Dsw2026Tpi.Api/Dsw2026Tpi.db*` y volver a iniciarla. Ese procedimiento borra todos los datos.

## Integración con el frontend

El frontend (React con Vite) corre en `http://localhost:5173` y llama a la API en `http://localhost:5278`.

- En desarrollo, el frontend usa el proxy `/api` de Vite, que reenvía las peticiones a esta API.
- CORS permite `http://localhost:5173` en `appsettings.Development.json`. `UseCors` corre antes de la autenticación, así que los preflight y las respuestas 401, 403 y 429 llevan los encabezados CORS.
- La sesión viaja en el encabezado `Authorization: Bearer <token>`, no en cookies.

Cambios del contrato para el frontend:

| Endpoint                            | Cambio                                                                |
| ----------------------------------- | --------------------------------------------------------------------- |
| `POST` y `PUT /api/doctors`         | El cuerpo usa `specialtyId`; `specialityId` ya no se acepta           |
| `GET /api/doctors/{id}`             | Nuevo, solo para Administrador; misma forma que un ítem del listado   |
| `GET /api/appointments/search`      | Cada resultado agrega `date`, `startTime` y `endTime`                 |

## Consigna

Acceso al [documento del TPI](https://frtutneduar-my.sharepoint.com/:b:/g/personal/franciscovicente_doc_frt_utn_edu_ar/IQD-5kaAARqnT5eL7EnPMCPgAX2LFXXX6e3p-u1C43z5rsQ?e=lbbpnz).

- Trabajar sobre una bifurcación por grupo y una rama de larga duración `development`.
- Organizar el trabajo mediante ramas temporales y actualizar `development` mediante pull requests.
- Mantener las migraciones de Identity y crear nuevas migraciones cuando corresponda.
- Provisionar el administrador inicial sin publicar credenciales ni un registro abierto.
