# AnxietyWatch Backend → ML Inference Client

The backend exposes a single reusable typed HTTP client for the secured AnxietyWatch ML service,
registered in `AnxietyWatch.Infrastructure`.

For **007-B4**, the client is invoked from the suspected-event flow (`POST /api/v1/events/suspected`):
a newly accepted suspected event triggers a window inference call as backend enrichment. `prediction`
is stored and exposed nowhere to the watch yet; it does not trigger SOS, caregiver notifications, or
any other product behavior. The ML result is a `Succeeded`/`SkippedNoTelemetry`/`Failed` enrichment
record persisted in `event_inferences`, keyed by `eventId`.

## ML endpoint

```text
POST {Ml:Inference:BaseUrl}/predict/window
```

## Configuration

All configuration is supplied through standard .NET configuration (environment variables or
user-secrets, never source control). Configuration is read once when the typed client instance is
constructed; there is no dynamic per-call secret reload in this MVP.

| Key | Purpose | Example env var |
| --- | --- | --- |
| `Ml:Inference:BaseUrl` | Absolute **HTTPS** base URL of the ML service. Non-HTTPS URLs are rejected with a `CONFIGURATION` failure and no request is sent. | `Ml__Inference__BaseUrl` |
| `Ml:Inference:ApiKey` | Secret value sent as the `X-Api-Key` header. **Configuration key name only; no secret value is committed.** | `Ml__Inference__ApiKey` |
| `Ml:Inference:TimeoutSeconds` | HTTP timeout, defaults to `10` | `Ml__Inference__TimeoutSeconds` |
| `Ml:Inference:TelemetryLookbackSeconds` | Raw telemetry lookback sent to ML, defaults to `60`. Must cover the deployed model's required event window. | `Ml__Inference__TelemetryLookbackSeconds` |
| `Ml:Inference:Retry:BaseDelaySeconds` | Exponential backoff base, defaults to `1` (1s, 2s) | `Ml__Inference__Retry__BaseDelaySeconds` |
| `Ml:Inference:Retry:MaxRetries` | Maximum retries after the initial attempt for transient failures, defaults to `2` | `Ml__Inference__Retry__MaxRetries` |

The API key is only ever sent as the `X-Api-Key` request header. It is never logged, never included
in exceptions or structured logs, and never serialized into request bodies or public DTOs.
Automatic HTTP redirects are disabled: the key is only ever sent to the configured ML endpoint.

If `PredictWindowAsync` is called without a valid `BaseUrl`/`ApiKey`, the client returns a
`CONFIGURATION` failure **without sending any HTTP request**. A missing key does not prevent the
backend from starting.

## Suspected-event integration (007-B4)

Inference runs in the suspected-event processing path, after the event has been durably accepted.
Sequence for a **new** `eventId`:

1. `TryStoreSuspectedEventAsync` stores the event. A duplicate `eventId` returns the existing
   duplicate response and **never** calls ML again.
2. The authenticated backend user is the window owner; telemetry is scoped by that user, plus the
   event's `deviceId` and `sessionId` (B2).
3. `windowEnd = detectedAt`, `windowStart = detectedAt - Ml:Inference:TelemetryLookbackSeconds`.
   This lookback only bounds how much raw source telemetry is fetched and sent; the ML service
   remains authoritative for final trimming and feature engineering. The backend lookback must cover
   the deployed model's required event window (v0.1.0 uses 60 seconds).
4. B2 raw samples are mapped 1:1 to `MlWindowSampleRequest` (`Timestamp`, `HeartRateBpm`, `IbiMs`,
   `SkinTemperatureCelsius`, `Quality`). No feature engineering, no derived watch features, no
   baseline/score, no accelerometer/ambient values, and no `userId` in the ML request.
5. If the window is empty, ML is not called and a `SkippedNoTelemetry` inference outcome is
   persisted. Local validation intentionally does not replicate ML rules (e.g. min sample counts,
   HR ratio).
6. The ML result is persisted in `event_inferences` keyed by `eventId`:
   - `Succeeded` stores `prediction`, `support_probability`, `threshold`, `model_version`, `target`.
   - `SkippedNoTelemetry` stores status only.
   - `Failed` stores the `MlInferenceFailureKind` classification.

Raw telemetry remains in `telemetry_batches`; inference documents store no raw physiology, no
request payload, no response body, no exception strings, and no API key.

### Failure safety

An ML failure never rejects an already-accepted suspected event:

- every failure kind (`Unauthorized`, `Validation`, `Transient`, `Unexpected`, `Configuration`) is
  persisted as `Failed` and the suspected-event response is unaffected;
- an unexpected client exception is caught at the integration boundary, logged safely, and persisted
  as a generic `Failed`/`Unexpected` outcome where possible;
- actual inbound-request cancellation propagates (matching existing handler behavior); ML timeouts
  are classified `Transient` by the client.

### Latency / MVP limitation

B4 currently performs inference synchronously in the suspected-event processing path (no background
queue). The endpoint can wait for the ML attempt after the event has already been persisted, bounded
by the B3 timeout/retry configuration. This is an accepted MVP limitation.

### Cancellation / reconciliation gap (MVP limitation)

Because inference runs synchronously inside the inbound request, if the client cancels the request
after the suspected event has been stored but before the ML attempt completes, cancellation
propagates into `RunInferenceAsync` and no inference record is persisted for that event. A later
duplicate submission of the same `eventId` is treated as a duplicate and does **not** re-trigger ML
(the "only first accepted event triggers inference" rule). In this MVP this is an accepted, documented
limitation: a retried event will simply not have an inference record.

This is intentionally **not** solved here with fire-and-forget tasks, `Task.Run`, or an in-memory
background queue (unbounded, lost on process restart, untracked). The intended fix is a durable
reconciliation design: persist an "attempted/pending" marker before awaiting the ML client, and
re-drive inference for stored-but-pending events outside the request lifetime (background worker /
queue). That work is deferred to a later milestone (007-B5+).

### Safe logging

Logs only include `eventId`, inference status, failure kind, `modelVersion`, and latency. API keys,
raw telemetry (HR, IBI, skin temperature), request/response bodies, user identity, and tokens are
never logged.

## Request/response contract

The request is sent with explicit camelCase property names and the response is read with explicit
snake_case property names (`support_probability`, `model_version`, ...). No ambient naming behavior
is relied on.

```json
{
  "eventId": "...",
  "deviceId": "...",
  "sessionId": "...",
  "detectedAt": "...",
  "samples": [
    {
      "timestamp": "...",
      "heartRateBpm": 72.5,
      "ibiMs": [810.0],
      "skinTemperatureCelsius": 33.2,
      "quality": { "heartRate": "good", "ibi": "fair", "wearingState": "onBody" }
    }
  ]
}
```

Response (parsed into `MlInferenceResponse`). All five members are required: a 2xx response missing
any of them, or failing semantic checks (prediction ∈ {0,1}; probabilities finite and in [0,1];
non-empty model version; target exactly `target_support_requested`), is classified `UNEXPECTED`
without retry and without logging the response body:

```json
{
  "prediction": 0,
  "support_probability": 0.001,
  "threshold": 0.003,
  "model_version": "0.1.0",
  "target": "target_support_requested"
}
```

`prediction` is preserved as-is. The backend does **not** reinterpret `prediction = 1` as anxiety,
panic, crisis, or SOS. The model target remains `target_support_requested`. Product decisions belong
to later work, not this client.

## Correlation

Every outgoing call carries:

```text
X-Correlation-Id: <eventId>
```

`eventId` is the natural correlation/idempotency key; no second identifier is introduced.

## Failures

Failures are returned as `MlInferenceResult` with a small classification:

- `SUCCESS`
- `UNAUTHORIZED` (HTTP 401/403)
- `VALIDATION` (HTTP 400/422)
- `TRANSIENT` (HTTP 408/429/500/502/503/504, network failure, timeout) — retried up to
  `Ml:Inference:Retry:MaxRetries` times with exponential backoff (1s, 2s by default)
- `UNEXPECTED` (other statuses, malformed or contract-invalid success payload)
- `CONFIGURATION` (BaseUrl/ApiKey missing or invalid)

Response bodies are never included in errors or logs.