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

El administrador inicial se crea mediante una configuración privada durante el arranque; no existe un endpoint público para registrar administradores. Los pacientes ingresan con email y DNI y su cuenta se crea automáticamente en el primer acceso.

La configuración, los flujos, las decisiones de seguridad y la prueba desde Visual Studio y Swagger se explican en [AUTHENTICATION.md](AUTHENTICATION.md).

### Clave JWT en desarrollo

La clave que firma los JWT no se guarda en el repositorio. Cada integrante la configura una vez con User Secrets, desde la raíz del repositorio:

```bash
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)" --project Dsw2026Tpi.Api
```

- La clave debe tener al menos 32 caracteres.
- Sin la clave, la API no arranca y muestra el error `JWT Key`.
- Si se cambia la clave, los tokens emitidos antes dejan de ser válidos y hay que volver a iniciar sesión.

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
