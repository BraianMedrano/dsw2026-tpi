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

No existe `POST /api/auth/admin/register`. El TPI solo necesita el administrador inicial y un registro público permitiría que cualquier persona se asigne privilegios. Si en el futuro el negocio requiere más administradores, deberá agregarse un caso de uso separado y protegido por `AdminPolicy`; no forma parte de esta feature.

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

## Endpoints públicos y protegidos

Solo estos endpoints son anónimos:

- `POST /api/auth/admin/login`
- `POST /api/auth/patient/login`

La política de autorización de respaldo exige autenticación para cualquier otro endpoint, incluso para uno nuevo que no declare `[Authorize]`. Las políticas `AdminPolicy` y `PatientPolicy` agregan la verificación del rol correspondiente. `/health-check` también requiere un token porque la consigna deja públicos únicamente los dos login.

- Sin token o con token inválido: `401`.
- Con token válido pero rol incorrecto: `403`.

## Probar desde Swagger

1. Ejecutar `Dsw2026Tpi.Api` con el perfil HTTPS de Visual Studio.
2. Ejecutar uno de los dos login desde Swagger.
3. Copiar el valor `token` de la respuesta.
4. Presionar **Authorize** y escribir `Bearer ` seguido del token.
5. Probar un endpoint protegido.

Los tests se pueden ejecutar antes o después de probar Swagger desde **Test Explorer**. Swagger no ejecuta tests automáticamente: es una prueba manual de la API en ejecución; Test Explorer ejecuta verificaciones repetibles y aisladas.

## Migraciones

`AuthenticationDbContext` conserva sus migraciones en `Dsw2026Tpi.Data/Migrations/Identity`. La migración `AddPatientDni` agrega la columna y el índice único.

En Development, el arranque aplica primero las migraciones de dominio y luego las de Identity. En otros ambientes deben aplicarse explícitamente durante el despliegue; la API no modifica el esquema automáticamente.
