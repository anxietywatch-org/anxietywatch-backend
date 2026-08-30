# Caregiver invitation flow

The family-plan owner authenticates with a bearer JWT and selects a patient already represented by an active `FamilyPlanPatientMembership`.

`POST /api/caregiver/patients/{patientId}/invitations` creates a `CaregiverInvitation` with `IssuedByUserId` from the JWT and `TargetPatientId` from the route. The application calls `IFamilyPlanPatientAuthorizer.CanManagePatientAsync`; an owner without an active membership receives `403`.

An authenticated caregiver accepts with `POST /api/caregiver/invitations/accept` and a body containing only `{ "code": "..." }`. The patient is never accepted from the request body or the caregiver JWT: it always comes from `CaregiverInvitation.TargetPatientId`. The resulting `CaregiverPatientLink` is `currentUser.UserId → TargetPatientId`, and acceptance does not issue or replace a JWT.

The new state is stored in Mongo collections `caregiver_invitations` and `caregiver_patient_links`. The link has a unique `(caregiverId, patientId)` index, so repeated acceptance is idempotent. `GET /api/caregiver/patients` combines explicit links with accepted legacy `family_member` links and de-duplicates by patient ID. The shared `ICaregiverAccessAuthorizer` accepts either relationship source, so patient detail, episodes, events, and latest telemetry/heart-rate retain centralized authorization.

Legacy `POST /api/tokens`, `POST /api/tokens/accept-by-code`, and `POST /api/caregiver/patients/link` remain available for existing onboarding and compatibility. Revoking a pending new invitation with `DELETE /api/caregiver/invitations/{id}` does not remove an already-created caregiver-patient link.
