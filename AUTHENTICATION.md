# Autenticación

Este documento explica cómo usar y entender la autenticación sin guardar credenciales reales en el repositorio.

## Administrador inicial

La aplicación crea un único administrador inicial durante el arranque:

1. `Dsw2026Tpi.Api → Program` inicia la aplicación.
2. `Dsw2026Tpi.Api → ApplicationStartupInitializer.InitializeAsync` aplica las migraciones en Development.
3. `Dsw2026Tpi.Api → InitialAdministratorBootstrapper.InitializeAsync` crea el usuario y le asigna el rol `Administrador`.

Las credenciales se leen desde User Secrets o variables de entorno. En Visual Studio:

1. Clic derecho sobre `Dsw2026Tpi.Api`.
2. Seleccionar **Manage User Secrets**.
3. Guardar valores locales con esta estructura, reemplazando los ejemplos:

```json
{
  "InitialAdministrator": {
    "Email": "administrador-local@example.com",
    "Password": "reemplazar-por-una-clave-segura"
  }
}
```

Como alternativa, las variables de entorno se llaman `InitialAdministrator__Email` e `InitialAdministrator__Password`. Nunca se deben copiar valores reales a `appsettings.json`, README, commits ni capturas.

No existe `POST /api/auth/admin/register`. El TPI solo necesita el administrador inicial y un registro público permitiría que cualquier persona se asigne privilegios. Si en el futuro el negocio requiere más administradores, deberá agregarse un caso de uso separado y protegido por `AdminPolicy`; no forma parte de esta funcionalidad.

## Login de administrador

`POST /api/auth/admin/login` recibe email y contraseña. Una respuesta válida devuelve:

- un JWT;
- el rol público `ADMINISTRADOR`.

Usuario inexistente, contraseña incorrecta y usuario sin rol administrador producen el mismo `401`. Esa respuesta uniforme evita revelar qué cuentas existen.

## Login y alta automática del paciente

`POST /api/auth/patient/login` recibe:

```json
{
  "email": "paciente@example.com",
  "dni": "12345678"
}
```

El DNI se modela como texto de 7 u 8 dígitos porque es un identificador, no un número para calcular.

- Si email y DNI no existen, se crea la cuenta y se asigna el rol `Paciente`.
- Si ambos pertenecen al mismo paciente, se reutiliza la cuenta.
- Si uno ya pertenece a otra identidad, se responde `401` sin indicar cuál dato produjo el conflicto.
- El índice único de DNI evita pacientes duplicados y la creación se completa dentro de una transacción para no dejar cuentas sin rol.

Una respuesta válida devuelve un JWT y el rol público `PACIENTE`.

## Cierre de sesión y revocación

Cada JWT emitido incluye un identificador único `jti`. `POST /api/auth/logout` requiere el token Bearer actual y, si es válido, guarda su `jti` y fecha de expiración en la tabla persistente `RevokedTokens`. La respuesta exitosa es `200` con `"ok"`.

La validación de autenticación consulta esa tabla en cada solicitud protegida. Un token revocado se rechaza con `401`, incluso después de reiniciar la API. Los tokens emitidos antes de incorporar esta funcionalidad no contienen `jti` y se rechazan intencionalmente porque no pueden revocarse de manera segura.

## Endpoints públicos y protegidos

Solo estos endpoints son anónimos:

- `POST /api/auth/admin/login`
- `POST /api/auth/patient/login`

`POST /api/auth/logout` y el resto de la API requieren autenticación. La política de autorización de respaldo protege incluso un endpoint nuevo que no declare `[Authorize]`. Las políticas `AdminPolicy` y `PatientPolicy` agregan la verificación del rol correspondiente. `/health-check` también requiere un token porque la consigna deja públicos únicamente los dos login.

- Sin token, con token inválido o con token revocado: `401`.
- Con token válido pero rol incorrecto: `403`.

## Límites de solicitudes

La API aplica ventanas fijas de un minuto sin cola. Cada solicitud consume un único límite:

| Operación | Límite | Partición |
|---|---:|---|
| Login de administrador | 5 por minuto | IP |
| Login de paciente | 10 por minuto | IP |
| Crear una cita | 5 por minuto | Paciente autenticado |
| Resto de los endpoints | 100 por minuto | Usuario autenticado o IP |

Los valores se configuran en `RateLimiting` dentro de `appsettings.json`. Al excederlos, la API responde `429` con el mismo contrato de error usado por el resto de la aplicación y registra el rechazo sin incluir credenciales ni tokens. La IP se toma de la conexión; `X-Forwarded-For` solo debe habilitarse en un despliegue que configure proxies confiables explícitamente.

## Probar desde Swagger

1. Ejecutar `Dsw2026Tpi.Api` con el perfil HTTPS de Visual Studio.
2. Ejecutar uno de los dos login desde Swagger.
3. Copiar el valor `token` de la respuesta.
4. Presionar **Authorize** y escribir `Bearer ` seguido del token.
5. Probar un endpoint protegido.
6. Ejecutar `POST /api/auth/logout` con ese mismo token.
7. Repetir la solicitud protegida y comprobar que responde `401`.
8. Para continuar, iniciar sesión nuevamente y reemplazar el token autorizado.

Los tests se pueden ejecutar antes o después de probar Swagger desde **Test Explorer**. Swagger no ejecuta tests automáticamente: es una prueba manual de la API en ejecución; Test Explorer ejecuta verificaciones repetibles y aisladas.

## Migraciones de autenticación

`AuthenticationDbContext` conserva sus migraciones en `Dsw2026Tpi.Data/Migrations/Identity`.

- `AddPatientDni` agrega la columna de DNI y su índice único.
- `AddRevokedTokens` agrega la tabla persistente utilizada por el cierre de sesión.

Ambos contextos comparten el archivo SQLite, pero mantienen migraciones independientes. El orden, los comandos y las precauciones para actualizar o reconstruir la base local están documentados en [Base de datos local](DEVELOPMENT.md#base-de-datos-local).
