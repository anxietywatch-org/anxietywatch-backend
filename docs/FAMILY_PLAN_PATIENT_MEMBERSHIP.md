# Family plan patient membership

`User.PlanId == "family"` indicates that an account can act as a family-plan
owner. It does not, by itself, authorize access to any particular patient.

The persistent source of truth is `family_plan_patient_memberships`:

```text
OwnerUserId + PatientUserId + Active = owner may manage patient
```

The unique owner/patient pair prevents duplicate memberships. The patient list
is available to an authenticated family owner at `GET /api/family/patients`.

The existing `LinkToken` remains an onboarding instrument. A `patient` token
continues to be available under the legacy token quota for all plans; only a
token whose existing owner has `PlanId == "family"` creates a family-plan
membership. Accepting such a token creates the patient account and an
idempotent owner-to-patient membership. Non-family owners keep the legacy
onboarding behavior but do not gain Family Plan authority. `family_member` and
`self` tokens do not create family-plan memberships. Revoking or expiring a
link token does not remove a membership.

Acceptance creates the non-self account with a stable identity derived from the
token id, then accepts the token, then ensures the membership. If membership
creation fails after acceptance, the startup reconciliation service can repair
the pair when the owner and accepted account exist. The operations are not one
Mongo transaction; this is eventual reconciliation, not atomicity.

The reconciliation service runs once at application startup, is idempotent,
does not modify tokens, and isolates invalid records so one record does not
prevent the remaining records from being processed.

The implementation deliberately does not enforce global uniqueness of a patient
across owners; only `OwnerUserId + PatientUserId` is unique for this MVP.
