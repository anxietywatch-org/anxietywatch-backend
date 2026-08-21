# AnxietyWatch — Wear/Fog → Backend → ML Handoff

**Version:** Technical ML MVP Complete (Backend develop `facd311`, ML develop `abb1548`)
**Date:** 2026-08-21
**Audience:** Wear/Fog teammates implementing real-device integration

---

## 1. Goal

Connect the Galaxy Watch sensor pipeline through the Mobile Fog Node to the Backend and ML inference service, such that:

- Detector-suspected events trigger real Azure ML inference
- User responses (`ACTIVITY_CONFIRMED`, `USER_OK`, `SUPPORT_REQUESTED`) become supervised labels linked by `eventId`
- Manual SOS remains a separate, independent flow
- Telemetry ordering guarantees (telemetry BEFORE suspected) are met
- No production synthetic data pollution

---

## 2. Architecture Diagram

```
Wear/Fog
   ↓
Backend
   ├──→ MongoDB
   │      telemetry persistence / query
   │
   └──→ raw event window over HTTPS
              ↓
            ML API
              ↓
      canonical preprocessing
              ↓
           features
              ↓
            model
```

**ML NEVER reads Backend Mongo directly.** Backend owns persistence/query and sends the raw event window to ML over HTTPS.

---

## 3. What Wear Owns

- Sensor data acquisition (HR, IBI, skin temp, accelerometer)
- `physicalActivity` flag from Samsung Health
- `PreliminaryDetector` → `USER_VALIDATION` state
- `PendingEvent` lifecycle
- Watch-side ACK handling for outbox
- `eventId` generation (UUID v4) — **must be reused for decision**
- `detectedAt` timestamp (event anchor)
- `SuspectedEventRequest` payload construction
- `EventDecisionRequest` payload construction
- Manual SOS trigger (`SosTriggerRequest`)

---

## 4. What Mobile/Fog Owns

- Accept `suspected` and `decision` deliverable kinds (currently missing!)
- Preserve all fields from Watch → Backend
- Enrich with authenticated `userId` where appropriate
- **Enforce delivery ordering:**
  1. Flush telemetry batches covering `[detectedAt-60s, detectedAt]`
  2. Wait for cloud ACK
  3. **Then** deliver `events/suspected`
- **Enforce suspected-before-decision ordering**
- Durable outbox with retry/backoff for `suspected`/`decision`
- Call backend endpoints with Bearer token
- Return ACK to Watch (cloud ACK → Watch ACK)

---

## 5. What Backend Already Owns (IMPLEMENTED)

- `POST /api/v1/telemetry/batch` — ingestion, validation, Mongo `telemetry_batches`
- `POST /api/v1/events/suspected` — B4 orchestration, idempotency, ML call, `event_inferences`
- `POST /api/v1/events/decision` — stores decision with `eventId`
- `POST /api/v1/sos/trigger` — manual SOS only, dispatches caregiver alerts
- `POST /api/v1/sos/cancel` — SOS cancellation
- User/device/session isolation (B2)
- Secure ML HTTP client (B3) — HTTPS, X-Api-Key, X-Correlation-Id, retries
- ML failure safety — never rejects suspected event
- Production Compose ML wiring (`docker-compose.prod.yml`)
- Full test suite (196/196 pass)

---

## 6. What ML Already Owns (IMPLEMENTED)

- `GET /health` → `model_loaded=true`, `model_version=0.1.0`
- `POST /predict` — single sample (dev)
- `POST /predict/window` — **canonical inference** (raw telemetry window)
- ML-owned preprocessing: 16-feature vector from RAW telemetry
- Training-serving parity (bundle config)
- API-key authentication
- Azure Container Apps deployment
- **NO direct Mongo access** — Backend sends raw window

---

## 7. Exact Endpoint Contracts

### 7.1 `POST /api/v1/telemetry/batch`

**Auth:** Bearer token (user)

**Request (JSON, camelCase):**
```json
{
  "batchId": "11111111-1111-1111-1111-111111111111",
  "deviceId": "22222222-2222-2222-2222-222222222222",
  "userId": null,
  "sessionId": "33333333-3333-3333-3333-333333333333",
  "startedAt": "2026-08-21T10:00:00.000Z",
  "endedAt": "2026-08-21T10:00:30.000Z",
  "sequence": 0,
  "samples": [
    {
      "timestamp": "2026-08-21T10:00:00.000Z",
      "heartRateBpm": 72.5,
      "ibiMs": [810.0, 820.0],
      "accelerometer": { "x": 0.0, "y": 0.0, "z": 9.81 },
      "skinTemperatureCelsius": 35.4,
      "ambientTemperatureCelsius": null,
      "quality": { "heartRate": "good", "ibi": "good", "wearingState": "onBody" }
    }
  ]
}
```

**Validation:**
- `batchId`, `deviceId`, `sessionId` required, not empty
- `sequence` ≥ 0
- `samples` 1–600 items
- `endedAt` ≥ `startedAt`
- Per sample: `ibiMs` ≤ 16 items, all > 0; `heartRateBpm` > 0 if present
- `quality.heartRate` ∈ [`good`,`fair`,`poor`,`unknown`]
- `quality.ibi` ∈ [`good`,`fair`,`poor`,`unknown`]
- `quality.wearingState` ∈ [`onBody`,`offBody`,`unknown`]

**Response:**
- `202 Accepted` — first acceptance: `{ "batchId": "...", "accepted": true, "duplicate": false }`
- `200 OK` — duplicate: `{ "batchId": "...", "accepted": false, "duplicate": true }`
- `400/401/403` — validation/auth errors

---

### 7.2 `POST /api/v1/events/suspected`

**Auth:** Bearer token (user)

**Request (JSON, camelCase):**
```json
{
  "eventId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "deviceId": "22222222-2222-2222-2222-222222222222",
  "userId": null,
  "sessionId": "33333333-3333-3333-3333-333333333333",
  "sequence": 0,
  "detectedAt": "2026-08-21T10:01:00.000Z",
  "state": "USER_VALIDATION",
  "score": 0.75,
  "rulesVersion": "rules-v2",
  "features": {
    "heartRateMean": 85.0,
    "heartRateMax": 102.0,
    "heartRateSlopeBpmPerMinute": 12.5,
    "heartRateDeltaFromBaseline": 18.0,
    "rmssdMillis": 18.0,
    "sdnnMillis": 25.0,
    "movementMagnitudeMean": 0.08,
    "movementVariance": 0.0012,
    "validSampleRatio": 0.92,
    "lastSampleAgeSeconds": 3,
    "sampleCount": 45
  },
  "baseline": {
    "sampleCount": 240,
    "meanHeartRate": 72.0,
    "heartRateM2": 200.0,
    "updatedAtEpochMillis": 1724234567890
  }
}
```

**⚠️ CRITICAL: `features`/`baseline` are AUDIT/PARITY ONLY. ML calculates its own 16-feature vector from RAW telemetry. Do NOT try to recreate ML features.**

**Validation:**
- `eventId`, `deviceId`, `sessionId` required, not empty
- `sequence` ≥ 0
- `detectedAt` required
- `state` non-empty, max 64 chars
- `score` ∈ [0,1]
- `rulesVersion` non-empty, max 64 chars
- `features.validSampleRatio` ∈ [0,1]
- `features.lastSampleAgeSeconds` ≥ 0
- `features.sampleCount` ≥ 0
- `features.heartRateSlopeBpmPerMinute` is a signed regression slope (negative values valid)
- `features.rmssdMillis` ≥ 0, `features.sdnnMillis` ≥ 0 if present
- `baseline` fields ≥ 0

**Response:**
- `202 Accepted` — first acceptance: `{ "eventId": "...", "accepted": true, "duplicate": false }`
- `200 OK` — duplicate: `{ "eventId": "...", "accepted": false, "duplicate": true }`
- **Duplicate `eventId` NEVER re-triggers ML inference.**

**Backend behavior on acceptance:**
1. Stores suspected event
2. Retrieves telemetry window `[detectedAt - 60s, detectedAt]` (B2 multi-batch)
3. Calls Azure ML `POST /predict/window` (B3 client)
4. Persists `EventInferenceResult` in `event_inferences` (keyed by `eventId`)

---

### 7.3 `POST /api/v1/events/decision`

**Auth:** Bearer token (user)

**Request (JSON, camelCase):**
```json
{
  "eventId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "deviceId": "22222222-2222-2222-2222-222222222222",
  "userId": "44444444-4444-4444-4444-444444444444",
  "sessionId": "33333333-3333-3333-3333-333333333333",
  "sequence": 0,
  "detectedAt": "2026-08-21T10:01:00.000Z",
  "respondedAt": "2026-08-21T10:01:15.000Z",
  "response": "SUPPORT_REQUESTED"
}
```

**Validation:**
- `eventId`, `deviceId`, `sessionId` required
- `sequence` ≥ 0
- `detectedAt`, `respondedAt` required, `respondedAt` ≥ `detectedAt`
- `response` ∈ [`ACTIVITY_CONFIRMED`, `USER_OK`, `SUPPORT_REQUESTED`] (case-insensitive)

**Response:**
- `202 Accepted` — first acceptance
- `200 OK` — duplicate
- `400` — invalid response enum, `respondedAt` < `detectedAt`

**Supervised label linkage:** Same `eventId` links telemetry window + suspected event + ML inference + decision.

---

### 7.4 `POST /api/v1/sos/trigger`

**Auth:** Bearer token (user)

**Request:**
```json
{
  "eventId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "deviceId": "22222222-2222-2222-2222-222222222222",
  "userId": "44444444-4444-4444-4444-444444444444",
  "triggeredAt": "2026-08-21T10:05:00.000Z",
  "source": "WATCH",
  "reason": "Manual SOS button press"
}
```

**Validation:**
- `eventId`, `deviceId` required
- `source` ∈ [`WATCH`, `MOBILE`] (case-insensitive)
- `reason` max 500 chars (optional)

**Response:** `202` / `200` (duplicate) — same pattern

**Behavior:** Dispatches caregiver alerts. **Manual SOS ONLY.**

---

### 7.5 `POST /api/v1/sos/cancel`

**Auth:** Bearer token (user)

**Request:**
```json
{
  "eventId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "deviceId": "22222222-2222-2222-2222-222222222222",
  "userId": "44444444-4444-4444-4444-444444444444",
  "cancelledAt": "2026-08-21T10:06:00.000Z",
  "reason": "False alarm"
}
```

**Response:** `202` / `200` (duplicate)

---

## 8. Exact Event Lifecycle

```
1. Wear collects telemetry (batches of ~30s)
2. Wear sends telemetry batch → Fog → Backend (202)
3. Wear detector escalates to USER_VALIDATION
4. Wear generates eventId (UUID v4)
5. Wear sends events/suspected with:
   - same eventId, deviceId, sessionId
   - detectedAt = event anchor timestamp
   - features/baseline (audit only)
6. Fog MUST ensure telemetry for [detectedAt-60s, detectedAt] is ACKed first
7. Backend accepts suspected event (202)
8. Backend retrieves telemetry window (B2 multi-batch)
9. Backend calls Azure ML /predict/window
10. Backend persists EventInferenceResult (event_inferences)
11. Wear user responds → SUPPORT_REQUESTED / ACTIVITY_CONFIRMED / USER_OK
12. Wear sends events/decision with SAME eventId + respondedAt
13. Fog delivers decision (after suspected is ACKed)
14. Backend stores decision linked by eventId
15. (Later) Manual SOS → separate eventId → /sos/trigger
```

---

## 9. eventId Correlation Rule

- **One `eventId` per detector escalation.**
- Generated by **Watch** (UUID v4).
- **Must be reused** for:
  - `events/suspected` (first)
  - `events/decision` (later, same `eventId`)
- **Different `eventId`** for manual SOS (`/sos/trigger`).
- `eventId` is the **sole correlation key** — no second identifier.

---

## 10. Telemetry-Before-Suspected Ordering Rule (CRITICAL)

**Backend B4 is synchronous:**
```
events/suspected arrives
    ↓
Backend IMMEDIATELY retrieves [detectedAt-60s, detectedAt]
    ↓
Calls ML
```

**Therefore:** Telemetry batches covering that window **MUST** be persisted in Backend **BEFORE** `events/suspected` arrives.

**Fog MUST:**
1. On receiving suspected event from Watch:
   - Flush all pending telemetry batches covering `[detectedAt-60s, detectedAt]`
   - Wait for Backend ACK (202) for each batch
2. **Only then** forward `events/suspected` to Backend

**Failure mode if violated:**
- Suspected arrives first → window empty → `SkippedNoTelemetry`
- `eventId` already accepted
- Duplicate suspected event **does not re-trigger ML**
- Event permanently has no inference

---

## 11. Suspected-Before-Decision Ordering Rule

- `events/decision` **must not** arrive before `events/suspected` for same `eventId`.
- Fog must queue decision until suspected ACK received.
- Backend accepts decision regardless (idempotent), but linkage requires suspected first.

---

## 12. Durable Outbox / ACK Requirements

Current Wear/Fog has ACK/retry for: `telemetry`, `sos`, `sos-cancel`.

**Must ADD for `suspected` and `decision`:**
- Fog enqueue support (deliverable kind)
- HTTP mapping to Backend endpoints
- Cloud ACK → Watch ACK
- Retry/backoff (exponential, max attempts)
- Cleanup on success
- **Idempotency:** Backend accepts duplicate `eventId` safely (returns 200, no re-trigger)

---

## 13. Manual SOS Separation

| Flow | Endpoint | eventId | Caregiver Alert |
|------|----------|---------|-----------------|
| Detector USER_VALIDATION | `/events/suspected` | UUID v4 (Watch) | **NO** |
| User Decision | `/events/decision` | **SAME** as suspected | **NO** |
| Manual SOS button | `/sos/trigger` | **NEW** UUID v4 | **YES** |
| SOS Cancel | `/sos/cancel` | Same as SOS trigger | N/A |

**Never** send detector event to `/sos/trigger`.
**Never** auto-generate SOS from `SUPPORT_REQUESTED`.

---

## 14. Physical Exercise Behavior

**Current (`physicalActivity` flag):**
- Only gates **baseline calibration updates** (prevents exercise HR from polluting baseline)
- **Does NOT prevent** `PreliminaryDetector` escalation to `USER_VALIDATION`
- Known exercise can still trigger suspected event

**Assessment:** Product/data-quality issue — exercise false alarms pollute data and UX.

**Recommended MVP behavior (A):**
> **Detector does not escalate to USER_VALIDATION during confidently known physical exercise.**

**Rationale:**
- Suppresses obvious exercise false alarms → better UX, cleaner data
- Tradeoff: Model training distribution is conditioned on detector-triggered events; suppressing some may shift distribution slightly. Acceptable for MVP.
- Alternative B (score adjustment) adds complexity; Alternative C (stay in OBSERVING) equivalent to A.

**⚠️ DO NOT** automatically convert Samsung Health exercise detection into `ACTIVITY_CONFIRMED` label. That is a **USER RESPONSE / supervised label**. Sensor-detected exercise must not fabricate ground truth.

**Important:** This recommendation is a **PRODUCT/DETECTOR policy recommendation**, NOT an ML contract requirement. The critical ML requirements are: correct raw telemetry, telemetry-before-suspected ordering, same eventId for suspected/decision, supported user decision enum, no accidental SOS routing. Exercise suppression can be implemented for MVP UX/data-quality reasons, but must not be confused with supervised ground truth.

---

## 15. Raw Telemetry Requirements (ML Compatibility)

Current `FeatureBuilder` v0.1.0 directly uses these fields from the raw telemetry window:

- `timestamp`
- `heart_rate_bpm`
- `ibi_ms`
- `skin_temperature_celsius`
- `quality_heart_rate`

Current 16-feature computation includes:
- HR statistics (mean, max, slope, delta from baseline)
- HRV (RMSSD, SDNN)
- IBI availability/coverage
- Skin-temp mean
- Heart-rate quality ratios
- Valid sample ratio
- Window duration
- Sample count

`quality_ibi` and `quality_wearing_state` are present in the internal telemetry contract / flattened dataframe, but **do NOT affect ANY current v0.1.0 feature or filtering behavior**. They are accepted/transported metadata, NOT currently part of the model feature vector.

| Field | Transport Status | ML Impact |
|-------|------------------|-----------|
| `timestamp` | ✅ AVAILABLE | Required |
| `heartRateBpm` | ✅ AVAILABLE | Required (≥30% presence) |
| `ibiMs` | ⚠️ AVAILABLE BUT SPARSE | Used if present |
| `skinTemperatureCelsius` | ⚠️ OFTEN NULL | Used if present |
| `quality.heartRate` | ✅ AVAILABLE | Required |
| `quality.ibi` | ✅ TRANSPORTED | Not currently used by v0.1.0 features |
| `quality.wearingState` | ⚠️ HARDCODED "unknown" | Not currently used by v0.1.0 features |
| `accelerometer` | ✅ AVAILABLE | **NOT used by current ML** |
| `ambientTemperatureCelsius` | ❌ NOT CAPTURED | NOT used by current ML |

**Current ML minimum for inference:**
- ≥ 10 samples in 60s window
- ≥ 30% HR presence

**DO NOT block ML if** accelerometer/ambient/derived features absent — ML doesn't use them.

**FLAG if** HR/IBI/timestamp/skin temp/quality.heartRate loss — these DO affect feature construction.

---

## 16. Real Sampling Rate Assessment

| Signal | Status |
|--------|--------|
| HR (passive) | **UNKNOWN / MUST MEASURE ON TARGET GALAXY WATCH** |
| IBI | availability/cadence device-dependent; **MUST MEASURE** |
| Skin temperature | availability/cadence device-dependent; **MUST MEASURE** |

**Do NOT state Samsung cadences as established facts** unless the current application code or an authoritative API contract explicitly guarantees them. The application code does not by itself prove the real hardware cadence.

**Current acceptance requirement remains:**
- ≥ 10 samples in the event window
- ≥ 30% HR presence

**The first real-device test MUST measure actual:**
- samples per 60 s
- HR-present samples
- IBI-bearing samples
- temperature-bearing samples
- time gaps between measurements

**No invented cadence.**

---

## 17. Model Input / Non-Input Clarification

| Category | Fields | Used by ML v0.1.0? |
|----------|--------|---------------------|
| **RAW telemetry (ML input)** | `timestamp`, `heartRateBpm`, `ibiMs`, `skinTemperatureCelsius`, `quality.heartRate` | ✅ YES |
| **Watch derived (audit only)** | `features.*`, `baseline.*` in suspected event | ❌ NO |
| **Watch sensor not sent** | accelerometer x/y/z, ambient temp | ❌ NO |
| **ML-internal** | 16-feature vector, bundle-derived threshold | Internal |

**Explicit warning to Wear developer:**
> DO NOT try to recreate the ML 16-feature request.
> DO NOT call Azure ML directly.
> DO NOT send Watch DerivedFeatures to `/predict`.
> Wear talks only to Fog/Backend.

---

## 18. Response / Label Semantics

| User Response | Backend Enum | ML Target Mapping (v0.1.0) | Creates SOS? |
|---------------|--------------|----------------------------|--------------|
| `SUPPORT_REQUESTED` | ✅ Supported | → target **1** (positive) | ❌ NO |
| `ACTIVITY_CONFIRMED` | ✅ Supported | → target **0** (negative) | ❌ NO |
| `USER_OK` | ✅ Supported | → target **0** (negative) | ❌ NO |
| `NO_RESPONSE` | ❌ Not supported | — | — |
| `BREATHING_HELPED` | ❌ Not supported | — | — |
| `SOS_REQUESTED` | ❌ Not supported (use `/sos/trigger`) | — | ✅ YES (manual) |
| `SOS_CANCELLED` | ❌ Not supported (use `/sos/cancel`) | — | — |

**Current backend `/events/decision` validator ONLY accepts:** `ACTIVITY_CONFIRMED`, `USER_OK`, `SUPPORT_REQUESTED`.

Do NOT send other enum values — will return 400.

---

## 19. Negative Slope Contract Mismatch

**ML:** `hr_slope_bpm_per_minute` can legitimately be **negative** (HR decreasing).

**Backend validator (current):** `SuspectedEventFeaturesRequestValidator` line 246:
```csharp
RuleFor(features => features.HeartRateSlopeBpmPerMinute).GreaterThanOrEqualTo(0)
    .When(features => features.HeartRateSlopeBpmPerMinute.HasValue);
```

**Verdict:** **PREREQUISITE BUG** — Backend rejects valid Watch-derived negative slopes.

**Fix:** Remove `GreaterThanOrEqualTo(0)` for `HeartRateSlopeBpmPerMinute` in `SuspectedEventFeaturesRequestValidator`. Do NOT tell Wear to clamp.

---

## 20. ACK/Retry Gap Analysis

| Deliverable | Fog Enqueue | HTTP Mapping | Cloud ACK | Watch ACK | Retry | Cleanup |
|-------------|-------------|--------------|-----------|-----------|-------|---------|
| Telemetry | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| SOS | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| SOS Cancel | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Suspected** | ❌ MISSING | ❌ MISSING | ❌ MISSING | ❌ MISSING | ❌ MISSING | ❌ MISSING |
| **Decision** | ❌ MISSING | ❌ MISSING | ❌ MISSING | ❌ MISSING | ❌ MISSING | ❌ MISSING |

**Required:** Add `suspected`/`decision` to Fog deliverable kinds, outbox, retry, ACK chain.

---

## 21. Low-Sample Mitigation — Correct Approach

**DO NOT** suggest reducing lookback, changing min-samples, or HR coverage requirement casually.

Reducing a 60-second lookback can REDUCE sample count further.

The serving pipeline derives canonical runtime behavior from the serialized ML bundle for training-serving parity.

**Correct mitigation:**
1. **FIRST** measure real-device telemetry.
2. If the real Watch consistently cannot satisfy the deployed bundle:
   - STOP and perform a deliberate ML/data-contract review.
3. Potential later solutions may include:
   - changing acquisition strategy/cadence
   - changing canonical window configuration
   - retraining/re-versioning the bundle
   but these require controlled evaluation.

**Wear/Fog must NOT independently change ML semantics.**

---

## 22. Watch Features/Baseline — Transmission Status

**AVAILABLE LOCALLY ON WATCH** ≠ **CURRENTLY TRANSMITTED END-TO-END.**

The deferred bug exists precisely because the corrected suspected-event route has NOT yet been implemented in Wear/Fog.

Do NOT mark Watch DerivedFeatures/Baseline as already delivered to Backend.

Expected future role: audit/parity/detector metadata only. NOT ML model input.

---

## 23. File-by-File Changes Expected

### apps/wear (Watch)

| File | Change |
|------|--------|
| `WearRuntime` / `PreliminaryDetector` | Add exercise suppression (A) — product/detector policy, not ML contract |
| `PendingEvent` | Emit `SuspectedEventRequest` with `eventId`, `detectedAt` |
| `PendingEvent` | Emit `EventDecisionRequest` with **same** `eventId`, `respondedAt` |
| `Outbox` / `MessageClient` | Add `suspected`/`decision` kinds, ACK handling |
| `SosTrigger` | Manual SOS only → `/sos/trigger` |

### apps/mobile / Fog

| File | Change |
|------|--------|
| `FogBridge` / `OutboxSyncer` | Add `suspected`/`decision` deliverable kinds |
| `FogNativeSync` / `FogBridge` | Enforce ordering: telemetry ACK → suspected → decision |
| `FogBridge` | Enrich with authenticated `userId` |
| `FogBridge` | Map to Backend endpoints, implement retry/backoff |
| `FogBridge` | Return cloud ACK → Watch ACK |

---

## 24. Acceptance Test Checklist (Real Watch)

| Test | Description | Pass Criteria |
|------|-------------|---------------|
| **TEST 1** | Normal telemetry | Batches reach Backend, timestamps/device/session correct |
| **TEST 2** | Detector fires | `events/suspected` sent (NOT SOS), preceding telemetry ACKed, `event_inferences` appears |
| **TEST 3** | `USER_OK` | `events/decision` with SAME `eventId`, no SOS |
| **TEST 4** | `ACTIVITY_CONFIRMED` | `events/decision` with SAME `eventId`, no SOS |
| **TEST 5** | `SUPPORT_REQUESTED` | `events/decision` with SAME `eventId`, support flow allowed, NO auto-SOS |
| **TEST 6** | Manual SOS | Only explicit manual SOS → `/sos/trigger`, caregiver notification |
| **TEST 7** | Duplicate/retry | Safe 200 duplicate ACK, no duplicate inference |
| **TEST 8** | Exercise | Known exercise behavior matches rule (A), never fabricate `ACTIVITY_CONFIRMED` |
| **TEST 9** | Real telemetry coverage | Inspect one real 60s window: ≥10 samples, ≥30% HR, IBI/temp recorded |

---

## 25. Common Failure Cases

| Failure | Cause | Mitigation |
|---------|-------|------------|
| `SkippedNoTelemetry` | Suspected arrived before telemetry | Enforce telemetry-before-suspected ordering |
| `VALIDATION` from ML | <10 samples or <30% HR | Ensure sufficient telemetry coverage |
| Duplicate inference missing | Request cancelled after event stored | Documented MVP limitation (B5+) |
| 400 on decision | Invalid `response` enum | Only send supported 3 values |
| Negative slope 400 | Backend validator bug | Fix backend validator (Part J) |
| SOS for detector event | Wrong endpoint | Use `/events/suspected`, not `/sos/trigger` |

---

## 26. Non-Clinical Disclaimer

**Model 0.1.0 is synthetic-data / academic MVP only.**

- `prediction = 1` → propensity for **SUPPORT_REQUESTED**, not anxiety/panic/crisis
- `prediction = 0` → propensity for **not SUPPORT_REQUESTED**, not "safe/all clear"
- **NO automatic SOS/caregiver action from ML prediction.**
- Product decisions belong to later work.

---

## 27. Team Ownership Split

| Layer | Owner | Key Responsibilities |
|-------|-------|---------------------|
| **Wear** | Watch teammate | Sensor pipeline, detector, event emission, ACK, `eventId` reuse |
| **Mobile/Fog** | Mobile teammate | Deliverable kinds, ordering, enrichment, outbox, Backend calls, ACK return |
| **Backend** | Backend team | **Already implemented** (unless validator bug found) |
| **ML** | ML team | **No changes expected** for this integration |

---

## 28. Recommended Release Tags (DO NOT CREATE)

- ML repo: `technical-ml-mvp-v1` → `abb1548`
- Backend repo: `technical-ml-integration-mvp-v1` → `facd311`

---

## 29. OpenAPI

Backend does not currently have a checked-in `docs/api/openapi.yaml`. The contracts above (Section 7) are the authoritative reference. If an OpenAPI spec is added later, it must be generated from current `develop` code to reflect:
- `POST /api/v1/events/suspected`
- `POST /api/v1/events/decision`
- Updated `SuspectedEventRequest` / `EventDecisionRequest` schemas

---

## 30. Confirmation

- ✅ No Wear/mobile application code changed in this task
- ✅ No Azure/DigitalOcean changes
- ✅ No secrets exposed
- ✅ Documentation only

---

## 31. GO / NO-GO for starting real-device integration

**GO** — Architecture is complete, contracts are stable, isolated acceptance passed.

**BLOCKERS to fix first:**
1. Backend negative-slope validator (Part J)
2. Fog `suspected`/`decision` deliverable kinds + ordering + ACK (Parts F, G)
3. Wear exercise suppression rule (Part E) — product/detector policy
4. Real-device telemetry coverage validation (Part 16)