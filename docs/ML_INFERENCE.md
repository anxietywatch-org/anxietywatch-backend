# AnxietyWatch Backend → ML Inference Client

The backend exposes a single reusable typed HTTP client for the secured AnxietyWatch ML service.
It is registered in `AnxietyWatch.Infrastructure` and is **not yet invoked** from suspected events
or any command handler. Nothing wires inference to product behavior in this change.

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
| `Ml:Inference:Retry:BaseDelaySeconds` | Exponential backoff base, defaults to `1` (1s, 2s) | `Ml__Inference__Retry__BaseDelaySeconds` |
| `Ml:Inference:Retry:MaxRetries` | Maximum retries after the initial attempt for transient failures, defaults to `2` | `Ml__Inference__Retry__MaxRetries` |

The API key is only ever sent as the `X-Api-Key` request header. It is never logged, never included
in exceptions or structured logs, and never serialized into request bodies or public DTOs.
Automatic HTTP redirects are disabled: the key is only ever sent to the configured ML endpoint.

If `PredictWindowAsync` is called without a valid `BaseUrl`/`ApiKey`, the client returns a
`CONFIGURATION` failure **without sending any HTTP request**. A missing key does not prevent the
backend from starting.

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