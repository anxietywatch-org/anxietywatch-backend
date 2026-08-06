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

## Despliegue en Render

El archivo `render.yaml` define un Web Service Docker con health check en `/health` y puerto `10000`.

1. Crear una base MongoDB en MongoDB Atlas.
2. En Atlas, permitir la conexión desde Render y crear la base `anxietywatch`.
3. En Render, seleccionar **New > Blueprint** y conectar este repositorio de GitHub.
4. Confirmar el servicio definido en `render.yaml`.
5. Completar `Mongo__ConnectionString` con la cadena de conexión de Atlas.
6. Mantener `Jwt__SigningKey` como variable generada por Render.
7. Esperar el deploy y comprobar `https://<servicio>.onrender.com/health`.

La configuración actual conserva usuarios, episodios y tokens en memoria; MongoDB se utiliza actualmente para el adaptador de planes. Por tanto, el despliegue es válido para demostración, pero requiere completar los repositorios MongoDB antes de usarlo como producción con datos persistentes.

La autenticación JWT, la revocación de tokens, los límites de plan y los primeros recursos protegidos están implementados como slices verticales. Los contratos restantes definidos en la especificación se incorporarán siguiendo el mismo patrón. No se almacenan secretos en `appsettings.json`.
