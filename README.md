# Trabajo Práctico Integrador
## Desarrollo de Software 2026

## Integrantes

- Braian Medrano — Legajo 57121
- Fernando Chumba — Legajo 53951
- Tobias Juarez Julio — Legajo 57424

## Orden de implementación del backend

1. Base común: capas, DTOs, manejo global de errores y migraciones.
2. Autenticación y autorización: usuarios, roles, JWT y administrador inicial.
3. Especialidades.
4. Médicos.
5. Disponibilidades y generación de slots de 30 minutos.
6. Citas: reserva, consulta y cancelación.
7. Búsquedas administrativas de turnos.
8. Pruebas, validaciones, logging y documentación final.

Las ramas se organizan por módulo funcional, no por capa técnica, para reducir dependencias entre integrantes.

Acceso al [documento](https://frtutneduar-my.sharepoint.com/:b:/g/personal/franciscovicente_doc_frt_utn_edu_ar/IQD-5kaAARqnT5eL7EnPMCPgAX2LFXXX6e3p-u1C43z5rsQ?e=lbbpnz)

Instrucciones:
* Realizar una bifurcación por grupo
* Crear una rama de larga duración `development`
* Completar `README` con los integrantes en cada bifurcación
* Todos los integrantes deben participar con confirmaciones en el repositorio bifurcado
* Organizar el trabajo en equipo y crear ramas temporales
* Actualizar la rama de larga duración mediante **pull-requests**
* No eliminar las ramas temporales
* Tener en cuenta que ya se realizaron las migraciones de Identity, crear nuevas de ser necesario
* Para más detalles, revisar la grabación de la última clase
* El endpoint de registración de usuarios administradores está disponible para crear usuarios y poder hacer pruebas, a futuro se eliminará
