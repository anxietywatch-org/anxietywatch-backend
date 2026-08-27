# Caregiver relationship audit

The `caregiver_relationship_audit` collection records successful caregiver
relationship lifecycle transitions without changing authorization. Authorization
continues to use accepted `link_tokens`; the audit is investigative evidence,
not a new relationship source of truth.

Each record contains only `auditId`, `patientId`, `caregiverId`,
`sourceTokenId`, `action`, and `occurredAt`. Actions are `AcceptedInitial`,
`AcceptedAdditional`, and `Revoked`. Invitation codes, JWTs, FCM tokens,
emails, passwords, telemetry, ML data, and Firebase credentials are never
stored or logged.

Mongo indexes support patient/caregiver/time and caregiver/time investigation.
There is no TTL index.

The business transition remains authoritative. The current deployment does not
assume Mongo transactions or a replica set, so link-token mutation and audit
insert are not atomic. If the audit insert fails after a successful transition,
the API preserves the successful business operation and emits a structured
error containing only the safe identifiers and action. This limitation is
intentional and must be considered when interpreting an incomplete audit trail.
