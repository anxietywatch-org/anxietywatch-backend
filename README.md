# AnxietyWatch API

Backend de AnxietyWatch sobre .NET 10 con Clean Architecture, DDD y CQRS. Servicio REST con JWT para `https://github.com/Dianacoquette/AnxietyWatch.Web.git`.

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

## Contrato de API (para el equipo del frontend)

Todas las respuestas y peticiones JSON usan nombres de campos en **camelCase**. Los endpoints protegidos requieren la cabecera:

```text
Authorization: Bearer <token>
```

### Errores

Los errores devuelven `application/problem+json` con esta forma:

```json
{
  "type": "https://httpstatuses.com/400",
  "title": "Mensaje del error",
  "status": 400,
  "traceId": "..."
}
```

Códigos usados: `400` validación, `401` credenciales/sesión inválidas, `403` cuota de plan superada o recurso ajeno, `404` no encontrado, `409` conflicto (email duplicado, token usado), `410` token de recuperación expirado, `429` demasiados intentos (incluye `Retry-After` en segundos).

### Autenticación

#### POST /api/auth/register — 201

```json
{
  "fullName": "Ana Pérez",
  "email": "ana@example.com",
  "password": "secret123",
  "planId": "free",
  "billingCycle": "monthly"
}
```

- `planId`: `free` | `individual` | `family` | `professional`.
- `billingCycle`: `monthly` | `yearly`.
- `paymentMethodToken` es **opcional** (la integración de pagos está pendiente); en el plan `free` debe omitirse.
- `fullName` 2-60 caracteres; `password` 8-30 caracteres (sin requisitos de mayúsculas/dígitos).
- `409` si el email ya está registrado.

```json
{
  "token": "<jwt>",
  "expiresAt": "2026-08-06T12:00:00Z",
  "user": {
    "id": "guid",
    "fullName": "Ana Pérez",
    "email": "ana@example.com",
    "planId": "free",
    "emailVerified": false,
    "avatarUrl": null
  }
}
```

#### POST /api/auth/login — 200

```json
{ "email": "ana@example.com", "password": "secret123" }
```

Devuelve el mismo `{ token, expiresAt, user }`. `401` con credenciales incorrectas; `429` tras 5 intentos fallidos (bloqueo de 60 segundos, cabecera `Retry-After`).

#### GET /api/auth/session — 200 (protegido)

Devuelve `{ token, expiresAt, user }` (misma forma que login, con un token nuevo) para restaurar la sesión en el cliente.

#### POST /api/auth/logout — 200 (protegido)

Sin cuerpo. Revoca el JWT actual; responde `{ "success": true }`.

#### POST /api/auth/password/forgot — 200

```json
{ "email": "ana@example.com" }
```

Respuesta genérica: `{ "message": "..." }` (siempre igual para no revelar emails existentes).

#### POST /api/auth/password/reset — 200

```json
{ "token": "<token-del-correo>", "newPassword": "nueva123" }
```

Responde `{ "message": "Password updated" }`. `410` si el token expiró (30 min) o ya se usó.

#### POST /api/auth/change-password — 200 (protegido)

```json
{ "currentPassword": "vieja123", "newPassword": "nueva123" }
```

#### GET /api/auth/verify-email/status — 200 (protegido)

```json
{ "emailVerified": false }
```

#### POST /api/auth/verify-email/resend — 200 (protegido)

Sin cuerpo. Genera un token de un solo uso válido por 24 horas y envía un correo HTML con un enlace `Email:VerificationUrl#token=...`. El fragmento evita exponer el token en logs HTTP y cabeceras `Referer`; el frontend debe retirarlo del navegador y enviarlo al endpoint de confirmación. Responde `{ "message": "Verification email sent" }`. Cooldown de 60 s → `429`.

#### POST /api/auth/verify-email/confirm — 200 (público)

```json
{ "token": "<token-del-enlace>" }
```

Responde `{ "message": "Email verified" }` y `GET /api/auth/verify-email/status` pasa a devolver `true`. El token se almacena únicamente como hash SHA-256, expira en 24 horas y sólo puede confirmarse una vez. Devuelve `410` si expiró, fue reemplazado por un reenvío o ya se utilizó.

### Planes

#### GET /api/plans — 200 (público)

```json
[
  {
    "id": "free",
    "name": "Gratuito",
    "priceMonthly": 0,
    "priceYearly": 0,
    "features": ["Dashboard"],
    "limitations": ["1 token"],
    "idealFor": "Uso personal"
  }
]
```

### Dashboard

#### GET /api/dashboard/summary — 200 (protegido)

```json
{
  "anxietyLevel": { "current": 3, "trend": "up" },
  "weeklyRecords": { "used": 2, "limit": 5 },
  "streakDays": 4,
  "exercisesCompleted": 0
}
```

`trend`: `up` | `down` | `stable`. `limit` es `null` en planes de pago.

### Episodios

#### GET /api/episodes?range=7|30|90 — 200 (protegido)

```json
[
  {
    "id": "guid",
    "date": "2026-08-06T12:00:00Z",
    "intensity": 3,
    "symptoms": ["palpitaciones"],
    "notes": null
  }
]
```

#### POST /api/episodes — 201 (protegido)

```json
{ "intensity": 3, "symptoms": ["palpitaciones"], "notes": "..." }
```

- `intensity` 0-100; `notes` máx. 500 caracteres.
- `403` en plan `free` al superar 5 episodios por semana.

### Tokens de vinculación (protegido)

#### GET /api/tokens — 200

```json
[
  {
    "id": "guid",
    "code": "AW-7K2P-9D4M-8Q2L",
    "role": "family_member",
    "expiresAt": "2026-09-05T12:00:00Z",
    "status": "pending"
  }
]
```

`role`: `self` | `family_member` | `patient`. `status`: `pending` | `accepted` | `deleted`. Vencen a los 30 días.

#### POST /api/tokens — 201

```json
{ "role": "family_member" }
```

Cuotas por plan: `free`/`individual` 1, `family` 5, `professional` 20. `403` si se supera la cuota.

#### DELETE /api/tokens/{id} — 200

Responde `{ "success": true }`. `409` si el token ya fue aceptado.

#### POST /api/tokens/{id}/accept — 200

```json
{ "deviceId": "dispositivo-abc" }
```

Responde `{ "status": "accepted" }`. `409` si está expirado o usado.

#### POST /api/tokens/{id}/share — 200

```json
{ "recipientEmail": "familiar@example.com" }
```

Responde `{ "sent": true }`.

#### GET /api/tokens/export — 200

Descarga `tokens.csv` (`text/csv`).

### Perfil y ajustes (protegido)

#### GET /api/profile — 200

Devuelve `{ "fullName": "...", "avatarUrl": null }` con los valores actuales.

#### PATCH /api/profile — 200

```json
{ "fullName": "Ana Pérez", "avatarUrl": null }
```

Responde `{ "fullName": "...", "avatarUrl": null }`.

#### GET /api/settings — 200

Devuelve `{ "anxietyThreshold": 70, "pushNotifications": true, "privateMode": false }` con las preferencias actuales.

#### PATCH /api/settings — 200

```json
{ "anxietyThreshold": 70, "pushNotifications": true, "privateMode": false }
```

Responde `{ "anxietyThreshold": 70, "pushNotifications": true, "privateMode": false }`. `403` si `privateMode: true` en plan `free`.

### Contenido

#### GET /api/content/faq — 200 (público)

```json
[{ "question": "...", "answer": "..." }]
```

#### GET /api/content/testimonials — 200 (público)

```json
[]
```

### CORS

Política `Frontend` habilitada con orígenes configurados en `Cors:AllowedOrigins` (comma-separated). Valores predeterminados para desarrollo del frontend: `http://localhost:5222,https://localhost:7130`.

### Fallback temporal para MongoDB Atlas

Mientras Atlas presenta un problema de conectividad, el compose de producción fuerza temporalmente `InMemory`.
Esta opción permite validar autenticación, planes, telemetría y SOS, pero los datos se perderán al reiniciar o
volver a desplegar el contenedor. Cuando Atlas esté disponible, restaure `Persistence__Provider: Mongo` en el
compose, configure `Persistence__Provider=Mongo` en el entorno de la Droplet y vuelva a desplegar mediante el pipeline.

## Despliegue en Render

El archivo `render.yaml` define un Web Service Docker con health check en `/health` y puerto `10000`. No requiere variables manuales: usa el proveedor InMemory y `Jwt__SigningKey` auto-generado.

1. En Render, seleccionar **New > Blueprint** y conectar este repositorio de GitHub.
2. Confirmar el servicio definido en `render.yaml` (rama `main`, auto-deploy en cada push).
3. Esperar el deploy y comprobar `https://<servicio>.onrender.com/health` → `{ "status": "ok" }`.

Nota: usuarios, episodios y tokens quedan en memoria (se reinician con cada deploy). Para persistir, definir `Persistence__Provider=Mongo` y `Mongo__ConnectionString` desde el dashboard. Para producción, definir `Cors__AllowedOrigins` con la URL del frontend (vacío = cualquier origen).
