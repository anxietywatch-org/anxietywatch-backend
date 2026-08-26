# AnxietyWatch API

Backend de AnxietyWatch sobre .NET 10 con Clean Architecture, DDD y CQRS. Servicio REST con JWT para `https://github.com/anxietywatch-org/anxietywatch-web`.

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

Códigos usados: `400` validación, `401` credenciales/sesión inválidas, `403` cuota de plan superada o recurso ajeno, `404` no encontrado, `409` conflicto (email duplicado, token usado), `410` token de recuperación expirado, `429` demasiados intentos y `503` proveedor temporalmente no disponible. `429` y `503` incluyen `Retry-After` en segundos.

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

Respuesta genérica: `{ "message": "..." }` (siempre `200` e igual para no revelar emails existentes). La búsqueda, creación del token y entrega se procesan en segundo plano; un fallo del proveedor se registra internamente sin cambiar la respuesta. El endpoint limita cada IP a 20 solicitudes por minuto, deduplica cada destinatario durante 60 segundos y devuelve `429` al exceder el límite por IP.

#### POST /api/auth/password/reset — 200

```json
{ "token": "<token-del-correo>", "newPassword": "nueva123" }
```

Responde `{ "message": "Password updated" }`. `410` si el token expiró (30 min) o ya se usó.

#### POST /api/auth/change-password — 200 (protegido)

```json
{ "currentPassword": "vieja123", "newPassword": "nueva123" }
```

El cambio persistido invalida los JWT emitidos anteriormente. La notificación por correo es best-effort y no convierte un cambio exitoso en un error HTTP.

#### GET /api/auth/verify-email/status — 200 (protegido)

```json
{ "emailVerified": false }
```

#### POST /api/auth/verify-email/resend — 200 (protegido)

Sin cuerpo. Genera un token de un solo uso válido por 24 horas y envía un correo HTML con un enlace `Email:VerificationUrl#token=...`. El fragmento evita exponer el token en logs HTTP y cabeceras `Referer`; el frontend debe retirarlo del navegador y enviarlo al endpoint de confirmación. Responde `{ "message": "Verification email sent" }`. Cooldown de 60 s → `429`. Un rechazo definitivo del proveedor revierte token/cooldown; un fallo de entrega devuelve `503` sin exponer detalles de Resend.

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

### Historial de eventos del paciente

#### GET /api/events?limit=50 — 200 (protegido)

Requiere Bearer JWT. El paciente se identifica exclusivamente con el usuario
autenticado (`ICurrentUser.UserId`); el cliente no envía `patientId` ni `userId`.
`limit` acepta `1..100` y por defecto es `50`. La respuesta se ordena por
`occurredAt DESC` y después `eventId DESC`; un historial vacío devuelve `200`
con `[]`.

El DTO contiene únicamente `eventId`, `type`, `occurredAt` y `status`:

```json
[
  {
    "eventId": "guid",
    "type": "SOS",
    "occurredAt": "2026-08-26T12:34:56Z",
    "status": "TRIGGERED"
  }
]
```

`SOS` es un evento SOS manual. `SUSPECTED_EVENT` representa el ciclo factual
de evento sospechoso, con estados como `DETECTED`, `ACTIVITY_CONFIRMED`,
`USER_OK` y `SUPPORT_REQUESTED`; `SUPPORT_REQUESTED` no es SOS. `ACTIVITY_CONFIRMED`
no confirma clínicamente ansiedad y `USER_OK` no es un diagnóstico ni una
clasificación del modelo. Una inferencia ML por sí sola no crea una fila.

Este endpoint no devuelve BPM, telemetría cruda, IBI, movimiento, temperatura,
identificadores de paciente/usuario/dispositivo/sesión, probabilidades o
scores ML, vectores de características, metadatos de modelo, tokens FCM,
datos de autenticación, score clínico ni anxiety score. Para una gráfica, el
cliente puede agregar eventos factuales por fecha u hora (fecha → cantidad de
eventos); no debe inventar un score clínico, de severidad, pánico o confianza ML.

`/api/episodes` conserva los registros de episodios guardados manualmente;
`/api/events` es el historial factual de eventos/alertas del paciente.

### Cuidador / familiar

El acceso de cuidador requiere una relación persistida aceptada con rol
`family_member`. El flujo usa la sesión propia autenticada del cuidador: puede
listar pacientes vinculados, añadir otros sin reemplazar su sesión y leer
detalles, episodios, eventos y telemetría reciente sólo de pacientes
autorizados.

#### GET /api/caregiver/patients — 200 (protegido)

Requiere Bearer JWT. Devuelve sólo pacientes de relaciones aceptadas
`family_member`, con `patientId`, `fullName`, `avatarUrl`, `role` y `linkedAt`.
Cero pacientes es válido y un cuidador puede tener varios. Relaciones
pendientes, revocadas, `self` o de rol `patient` quedan excluidas.

#### POST /api/caregiver/patients/link — 200 (protegido)

Usa el JWT existente del cuidador y recibe `{ "code": "AW-..." }`. El código
debe corresponder a una invitación pendiente y no expirada creada por el
paciente con rol `family_member`; no se debe usar un código de rol `patient`.
El rol representa el acceso concedido al cuidador, aunque la interfaz diga
“añadir paciente”. El endpoint no crea otra cuenta, no reemplaza el JWT o la
sesión y conserva las relaciones existentes, por lo que permite vincular
pacientes adicionales al mismo cuidador.

#### GET /api/caregiver/patients/{patientId} — 200 (protegido)

Requiere Bearer JWT y valida la relación persistida antes de consultar al
paciente. Devuelve sólo `patientId`, `fullName` y `avatarUrl`.

#### GET /api/caregiver/patients/{patientId}/episodes?range=7 — 200

`range` admite `7`, `30` o `90` días y por defecto es `7`. Con `PrivateMode=false`
puede devolver `symptoms` y `notes`; con `PrivateMode=true` ambos son `null` y
`detailsHidden=true`. Un estado PrivateMode legado no resoluble falla cerrado.
PrivateMode no modifica los contratos de eventos ni de telemetría.

#### GET /api/caregiver/patients/{patientId}/events?limit=50 — 200

`limit` admite `1..100` y por defecto es `50`; la línea de tiempo es más nueva
primero. El DTO contiene sólo `eventId`, `type`, `occurredAt` y `status`.
Evento sospechoso y decisión con el mismo `eventId` forman un elemento lógico;
SOS tiene su propio ciclo de trigger/cancelación. `SUPPORT_REQUESTED` no es SOS,
la inferencia sola no se expone y no se devuelve telemetría cruda, probabilidad
ni score ML.

#### GET /api/caregiver/patients/{patientId}/telemetry/latest — 200/204

Es la ruta canónica; `/api/caregiver/patients/{patientId}/heart-rate/latest`
es un alias de compatibilidad y ambos tienen la misma conducta. Con BPM
positivo usable devuelve `200`:

```json
{
  "heartRateBpm": 82,
  "measuredAt": "2026-08-25T20:30:00Z",
  "ageSeconds": 18,
  "quality": "good"
}
```

Si nunca hubo telemetría, no hay datos de wearable sincronizados o no existe
BPM positivo usable, devuelve `204 No Content` (no `500` y no un punto falso
con BPM `0`). Es telemetría informativa sin interpretación clínica; no incluye
IBI, movimiento, temperatura, identificadores de dispositivo/sesión ni campos ML.

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

Devuelve los valores actuales del perfil. Los campos médicos son opcionales (`null` si no se han configurado):

```json
{
  "fullName": "Ana Pérez",
  "avatarUrl": null,
  "allergies": null,
  "currentMedications": null,
  "emergencyContactName": null,
  "emergencyContactPhone": null,
  "previousAnxietyDiagnosis": null,
  "treatingProfessional": null
}
```

#### PATCH /api/profile — 200

```json
{ "fullName": "Ana Pérez", "avatarUrl": null }
```

Todos los campos son opcionales en la petición. Campos médicos admitidos: `allergies`, `currentMedications`, `emergencyContactName`, `emergencyContactPhone`, `previousAnxietyDiagnosis` (bool), `treatingProfessional`. Responde con el JSON completo igual al GET.

#### GET /api/settings — 200

Devuelve `{ "anxietyThreshold": 70, "pushNotifications": true, "privateMode": false }` con las preferencias actuales.

#### PATCH /api/settings — 200

```json
{ "anxietyThreshold": 70, "pushNotifications": true, "privateMode": false }
```

Responde `{ "anxietyThreshold": 70, "pushNotifications": true, "privateMode": false }`. `403` si `privateMode: true` en plan `free`.

### Dispositivos y notificaciones push (protegido)

#### POST /api/devices/register — 200

Registra el token FCM en la cuenta identificada exclusivamente por el JWT Bearer.
El cliente no envía `userId`. Debe llamarse después de que exista una sesión
autenticada y cada vez que FCM entregue un token nuevo o actualizado.

```json
{ "platform": "android", "token": "fcm-token-del-dispositivo" }
```

`platform`: `android` | `ios` | `web`.

Responde sin exponer el token:

```json
{
  "id": "guid",
  "platform": "android",
  "registeredAt": "2026-08-25T20:00:00Z",
  "updatedAt": "2026-08-25T20:00:00Z"
}
```

Registrar el mismo token nuevamente es idempotente y actualiza plataforma,
propietario y `updatedAt`. Si una nueva cuenta registra un token ya conocido,
la propiedad se transfiere atómicamente a esa cuenta. Una cuenta puede mantener
varios tokens; sin `installationId`, la rotación se registra como un token nuevo.

#### POST /api/devices/unregister — 200

```json
{ "token": "fcm-token-del-dispositivo" }
```

Responde `{ "success": true }`.

#### GET /api/devices — 200

Lista metadatos de los destinos registrados de la cuenta autenticada. Los tokens
FCM no se incluyen en ninguna respuesta pública.

Para cerrar sesión o retirar el dispositivo, el cliente puede llamar
`POST /api/devices/unregister` con su token actual antes de desecharlo.

Las alertas se guardan primero en un outbox durable, una por dispositivo, y un
worker las entrega por Firebase. Solo reciben alertas los cuidadores con una
relación persistida `Accepted` y rol `family_member`; la relación y la propiedad
del dispositivo se vuelven a validar inmediatamente antes del envío.

| Evento | Push |
| --- | --- |
| SOS manual | Sí |
| `SUPPORT_REQUESTED` | Sí |
| `USER_OK` | No |
| `ACTIVITY_CONFIRMED` | No |
| Evento sospechoso antes de respuesta | No |
| Inferencia ML por sí sola | No |

`SUPPORT_REQUESTED` no es SOS. El payload `data` contiene exactamente
`eventId`, `patientName` y `alertMessage`; agrega `emergencyPhone` solo cuando
existe en el perfil persistido y `location` solo cuando existe una fuente
persistida fiable (actualmente se omite). Nunca contiene telemetría, resultados
ML, JWT ni tokens FCM.

Firebase está deshabilitado por defecto. El entorno autorizado debe configurar
`Firebase__Enabled=true`, `Notifications__WorkerEnabled=true` y exactamente una
de `Firebase__CredentialsPath` (ruta a un secreto montado fuera de la imagen) o
`Firebase__CredentialsJson` (JSON suministrado por el gestor de secretos).
`Firebase__ProjectId` es opcional. Si Firebase se habilita sin una fuente de
credenciales válida, el proceso falla claramente al arrancar. CI y desarrollo
no requieren credenciales. Tras cambiar estos valores se debe reiniciar o
redesplegar el API; nunca se debe guardar la Service Account en Git, imágenes o
logs.

### Ingesta Wearable (contratos estables)

Los cinco endpoints protegidos de escritura son:

- `POST /api/v1/telemetry/batch`
- `POST /api/v1/sos/trigger`
- `POST /api/v1/sos/cancel`
- `POST /api/v1/events/suspected`
- `POST /api/v1/events/decision`

Las escrituras aceptadas son idempotentes: la primera inserción responde `202`
y un duplicado responde `200`. Para un mismo `eventId`, el orden conceptual es
ACK de telemetría → ACK de sospecha → ACK de decisión. SOS mantiene un ciclo
independiente. Las decisiones admitidas son `ACTIVITY_CONFIRMED`, `USER_OK` y
`SUPPORT_REQUESTED`; este último no es SOS.

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

## DevOps

El despliegue operativo está documentado en [docs/DEVOPS.md](docs/DEVOPS.md). La rama desplegada de la API pública es `develop` y el endpoint estable es `https://api.mangoon.xyz`.
