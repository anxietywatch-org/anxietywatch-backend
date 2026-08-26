# Caregiver API Handoff

Base path: `/api`

All protected endpoints require `Authorization: Bearer <token>`. Responses use
the API's normal JSON camelCase serialization.

## Redeem Invitation

`POST /api/tokens/accept-by-code`

Authentication: anonymous.

Request:

```json
{
  "code": "AW-EXAMPLE",
  "deviceId": "client-device-id"
}
```

Success: `200 OK` with a temporary caregiver token, expiry, role, and user
projection. The response includes the temporary account user id.

Common errors: `400` invalid request, `404` unknown code, `409` already used or
unavailable code, `410` expired code.

## Activate Caregiver

`POST /api/auth/caregiver/activate`

Authentication: temporary caregiver token required.

Request:

```json
{
  "email": "caregiver@example.com",
  "password": "password"
}
```

Success: `200 OK` with a replacement normal authentication response. Keep the
same user id; use the returned token for subsequent login/session operations.

Common errors: `400` invalid email/password, `401` invalid session, `409`
email conflict or already activated.

## Login

`POST /api/auth/login`

Authentication: anonymous.

Request:

```json
{
  "email": "caregiver@example.com",
  "password": "password"
}
```

Success: `200 OK` with an authentication response containing token, expiry, and
safe user projection. Common errors: `400` invalid request, `401` invalid
credentials.

## Session

`GET /api/auth/session`

Authentication: required.

Success: `200 OK` with the current authentication response. Common error:
`401` unauthenticated or revoked session.

## Linked Patients

`GET /api/caregiver/patients`

Authentication: required.

Success: `200 OK` with only accepted `family_member` relationships:

```json
[
  {
    "patientId": "guid",
    "fullName": "Patient",
    "avatarUrl": null,
    "role": "family_member",
    "linkedAt": "2026-08-25T20:00:00Z"
  }
]
```

Common errors: `401` unauthenticated. Unlinked, pending, revoked, self, and
patient-role relationships are not returned.

## Link an Additional Patient

`POST /api/caregiver/patients/link`

Authentication: an existing `family_member` caregiver session is required.
The caregiver identity comes exclusively from the active JWT.

Request:

```json
{
  "code": "AW-EXAMPLE"
}
```

Success: `200 OK` with `patientId`, `fullName`, `avatarUrl`, `role`, and
`linkedAt`. The endpoint accepts only a pending, unexpired `family_member`
invitation belonging to a patient account. It does not create a user, replace
the JWT, or alter any existing patient relationship.

Common errors: `400` invalid request, `401` unauthenticated, `403` the current
account is not a caregiver, `404` unknown code/invitation owner missing, `409`
ineligible, unavailable, or already-used code, `410` expired code.

## Patient Detail

`GET /api/caregiver/patients/{patientId}`

Authentication: required. Persisted caregiver authorization is checked before
patient lookup.

Success: `200 OK` with `patientId`, `fullName`, and `avatarUrl` only. Common
errors: `401` unauthenticated, `403` not authorized, `404` patient not found.

## Patient Episodes

`GET /api/caregiver/patients/{patientId}/episodes?range=7`

Authentication: required. `range` accepts only `7`, `30`, or `90` days; the
default is `7`.

Success: `200 OK` with the episode list. With `PrivateMode=false`, `symptoms`
and `notes` are returned. With `PrivateMode=true`, they are `null` and
`detailsHidden=true`. An unresolved legacy PrivateMode state fails closed the
same way.

Common errors: `400` invalid range, `401` unauthenticated, `403` not
authorized.

## Patient Events

`GET /api/caregiver/patients/{patientId}/events?limit=50`

Authentication: required. `limit` accepts `1..100`; the default is `50`.

Success: `200 OK` with a globally bounded, newest-first timeline. Suspected
events and decisions sharing an `eventId` are one logical item. `SUPPORT_REQUESTED`
is a suspected-event decision, not SOS. SOS trigger and cancellation form an
independent SOS lifecycle.

The response contains only `eventId`, `type`, `occurredAt`, and `status`.
Inference, reasons, raw telemetry, identifiers, and ML fields are excluded.

Common errors: `400` invalid limit, `401` unauthenticated, `403` not
authorized.

## Latest Patient Telemetry

Canonical: `GET /api/caregiver/patients/{patientId}/telemetry/latest`

Temporary compatibility alias:
`GET /api/caregiver/patients/{patientId}/heart-rate/latest`

Authentication: required. Caregiver authorization runs before telemetry access.
Both routes execute the same handler and return the same DTO and status semantics.

Success: `200 OK`:

```json
{
  "heartRateBpm": 82,
  "measuredAt": "2026-08-25T20:30:00Z",
  "ageSeconds": 18,
  "quality": "good"
}
```

The endpoint selects the newest persisted sample with a positive BPM. If no
usable BPM exists, it returns `204 No Content`. This is informational
telemetry only and provides no clinical interpretation or freshness judgment.

The response does not contain batch, device, session, user, IBI, movement,
temperature, raw sample, or ML fields.

Common errors: `401` unauthenticated, `403` not authorized.

## Revoke Caregiver Relationship

`POST /api/tokens/{tokenId}/revoke`

Authentication: patient account required. The patient revokes the relationship
identified by their token id.

Success: `200 OK` with `{ "success": true }`. The affected caregiver loses
access immediately, including with the same JWT. Other caregiver relationships
and the caregiver account remain valid.

Common errors: `401` unauthenticated, `403` not the token owner, `404` unknown
token.

## Integration Notes

- The caregiver relationship authority is persisted `LinkToken.UserId` as the
  patient and `AcceptedBy` as the caregiver, with accepted status and the exact
  `family_member` role.
- Do not use the caregiver endpoints as a replacement for wearable write APIs.
- The five wearable write contracts remain unchanged.
- `SUPPORT_REQUESTED` is not SOS.
- Episode PrivateMode redacts only `symptoms` and `notes`; it does not change
  the documented heart-rate endpoint contract.
