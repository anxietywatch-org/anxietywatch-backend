using System.Text.Json.Serialization;

namespace AnxietyWatch.Application.Abstractions.MlInference;

public enum MlInferenceFailureKind
{
    Unauthorized,
    Validation,
    Transient,
    Unexpected,
    Configuration
}

public sealed record MlInferenceResult(MlInferenceResponse? Response, MlInferenceFailureKind? FailureKind)
{
    public bool IsSuccess => FailureKind is null;

    public static MlInferenceResult Success(MlInferenceResponse response) => new(response, null);

    public static MlInferenceResult Failure(MlInferenceFailureKind kind) => new(null, kind);
}

public sealed record MlWindowQualityRequest(
    [property: JsonPropertyName("heartRate")] string HeartRate,
    [property: JsonPropertyName("ibi")] string Ibi,
    [property: JsonPropertyName("wearingState")] string WearingState);

public sealed record MlWindowSampleRequest(
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("heartRateBpm")] double? HeartRateBpm,
    [property: JsonPropertyName("ibiMs")] IReadOnlyList<double> IbiMs,
    [property: JsonPropertyName("skinTemperatureCelsius")] double? SkinTemperatureCelsius,
    [property: JsonPropertyName("quality")] MlWindowQualityRequest Quality);

public sealed record MlWindowInferenceRequest(
    [property: JsonPropertyName("eventId")] Guid EventId,
    [property: JsonPropertyName("deviceId")] Guid DeviceId,
    [property: JsonPropertyName("sessionId")] Guid SessionId,
    [property: JsonPropertyName("detectedAt")] DateTimeOffset DetectedAt,
    [property: JsonPropertyName("samples")] IReadOnlyList<MlWindowSampleRequest> Samples);

public sealed record MlInferenceResponse(
    [property: JsonPropertyName("prediction"), JsonRequired] int Prediction,
    [property: JsonPropertyName("support_probability"), JsonRequired] double SupportProbability,
    [property: JsonPropertyName("threshold"), JsonRequired] double Threshold,
    [property: JsonPropertyName("model_version"), JsonRequired] string ModelVersion,
    [property: JsonPropertyName("target"), JsonRequired] string Target);

public interface IMlInferenceClient
{
    Task<MlInferenceResult> PredictWindowAsync(
        MlWindowInferenceRequest request,
        CancellationToken cancellationToken = default);
}