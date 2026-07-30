# AnxietyWatch API

Base de AnxietyWatch sobre .NET 10 con Clean Architecture, DDD y CQRS.

## Proyectos

- `src/AnxietyWatch.Domain`: modelo y reglas de dominio.
- `src/AnxietyWatch.Application`: casos de uso MediatR y puertos de aplicación.
- `src/AnxietyWatch.Infrastructure`: adaptadores MongoDB, caché y persistencia de desarrollo.
- `src/AnxietyWatch.Api`: composición HTTP y middleware.
- `tests`: pruebas de dominio, aplicación, integración y seguridad.

## Ejecución local

La configuración predeterminada usa el repositorio in-memory para que la API pueda iniciarse sin secretos ni servicios externos.

```powershell
dotnet run --project src/AnxietyWatch.Api/AnxietyWatch.Api.csproj
```

Endpoint implementado en esta base:

```text
GET /api/plans
POST /api/auth/register
POST /api/auth/login
GET /api/auth/session
POST /api/auth/logout
POST /api/auth/password/forgot
POST /api/auth/password/reset
POST /api/auth/change-password
GET /api/auth/verify-email/status
POST /api/auth/verify-email/resend
GET /api/dashboard/summary
GET /api/episodes?range=7|30|90
POST /api/episodes
GET /api/tokens
POST /api/tokens
DELETE /api/tokens/{id}
POST /api/tokens/{id}/accept
POST /api/tokens/{id}/share
GET /api/tokens/export
PATCH /api/profile
PATCH /api/settings
GET /api/content/faq
GET /api/content/testimonials
```

Para activar MongoDB, defina los valores fuera del repositorio:

```powershell
$env:Persistence__Provider = "Mongo"
$env:Mongo__ConnectionString = "<secret-from-key-vault>"
$env:Mongo__DatabaseName = "anxietywatch"
$env:Jwt__SigningKey = "<secret-with-at-least-32-bytes>"
```

En `Development` y `Testing`, si `Jwt__SigningKey` no existe, se genera una clave efímera en memoria. En `Production` la aplicación falla al arrancar para impedir un despliegue sin una clave administrada por Key Vault, Secrets Manager u otro gestor equivalente.

## Verificación

```powershell
dotnet build AnxietyWatchAPI.slnx --configuration Release
dotnet test AnxietyWatchAPI.slnx --configuration Release
dotnet list AnxietyWatchAPI.slnx package --vulnerable --include-transitive
```

La autenticación JWT, la revocación de tokens, los límites de plan y los primeros recursos protegidos están implementados como slices verticales. Los contratos restantes definidos en la especificación se incorporarán siguiendo el mismo patrón. No se almacenan secretos en `appsettings.json`.
