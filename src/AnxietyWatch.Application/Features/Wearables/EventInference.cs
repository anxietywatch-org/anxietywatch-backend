using System.Text.Json.Serialization;
using AnxietyWatch.Application.Abstractions.MlInference;

namespace AnxietyWatch.Application.Features.Wearables;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EventInferenceStatus
{
    Succeeded,
    SkippedNoTelemetry,
    Failed
}

public sealed record EventInferenceResult(
    Guid EventId,
    EventInferenceStatus Status,
    int? Prediction,
    double? SupportProbability,
    double? Threshold,
    string? ModelVersion,
    string? Target,
    MlInferenceFailureKind? FailureKind,
    DateTimeOffset AttemptedAt);

public interface IEventInferenceRepository
{
    Task<bool> TryStoreInferenceAsync(
        Guid userId,
        EventInferenceResult result,
        CancellationToken cancellationToken = default);

    Task<EventInferenceResult?> GetInferenceAsync(
        Guid userId,
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public interface ISuspectedEventInferenceService
{
    Task RunInferenceAsync(
        Guid userId,
        SuspectedEventRequest suspectedEvent,
        CancellationToken cancellationToken = default);
}