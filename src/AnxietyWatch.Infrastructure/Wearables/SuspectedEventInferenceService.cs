using System.Diagnostics;
using AnxietyWatch.Application.Abstractions.MlInference;
using AnxietyWatch.Application.Features.Wearables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.Wearables;

public sealed class SuspectedEventInferenceService(
    ILogger<SuspectedEventInferenceService> logger,
    IWearableSyncRepository wearableRepository,
    IEventInferenceRepository inferenceRepository,
    IMlInferenceClient mlInferenceClient,
    IConfiguration configuration) : ISuspectedEventInferenceService
{
    private readonly TimeSpan _telemetryLookback =
        TimeSpan.FromSeconds(ParseLookback(configuration["Ml:Inference:TelemetryLookbackSeconds"], 60));

    public async Task RunInferenceAsync(
        Guid userId,
        SuspectedEventRequest suspectedEvent,
        CancellationToken cancellationToken = default)
    {
        var eventId = suspectedEvent.EventId;
        var latency = Stopwatch.StartNew();
        try
        {
            var windowEnd = suspectedEvent.DetectedAt;
            var windowStart = windowEnd - _telemetryLookback;
            var window = await wearableRepository.GetTelemetryWindowAsync(
                userId,
                suspectedEvent.DeviceId,
                suspectedEvent.SessionId,
                windowStart,
                windowEnd,
                cancellationToken);

            if (window.Samples.Count == 0)
            {
                latency.Stop();
                logger.LogInformation(
                    "ML inference skipped for event {EventId}: no telemetry in the {LookbackSeconds}s lookback window.",
                    eventId,
                    _telemetryLookback.TotalSeconds);
                await TryPersistSkippedAsync(userId, eventId, cancellationToken);
                return;
            }

            var request = new MlWindowInferenceRequest(
                eventId,
                suspectedEvent.DeviceId,
                suspectedEvent.SessionId,
                suspectedEvent.DetectedAt,
                MapSamples(window.Samples));

            var result = await mlInferenceClient.PredictWindowAsync(request, cancellationToken);
            latency.Stop();

            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "ML inference succeeded for event {EventId} (model {ModelVersion}) after {LatencyMs}ms.",
                    eventId,
                    result.Response!.ModelVersion,
                    latency.ElapsedMilliseconds);
                await TryPersistSuccessAsync(userId, eventId, result.Response, cancellationToken);
                return;
            }

            logger.LogWarning(
                "ML inference failed for event {EventId} with {FailureKind} after {LatencyMs}ms.",
                eventId,
                result.FailureKind,
                latency.ElapsedMilliseconds);
            await TryPersistFailureAsync(userId, eventId, result.FailureKind, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            latency.Stop();
            logger.LogWarning(
                "ML inference timed out for event {EventId} after {LatencyMs}ms.",
                eventId,
                latency.ElapsedMilliseconds);
            await TryPersistFailureAsync(userId, eventId, MlInferenceFailureKind.Transient, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            latency.Stop();
            logger.LogError(
                exception,
                "ML inference crashed unexpectedly for event {EventId} after {LatencyMs}ms.",
                eventId,
                latency.ElapsedMilliseconds);
            await TryPersistFailureAsync(userId, eventId, MlInferenceFailureKind.Unexpected, cancellationToken);
        }
    }

    private async Task TryPersistSkippedAsync(Guid userId, Guid eventId, CancellationToken cancellationToken)
    {
        try
        {
            await inferenceRepository.TryStoreInferenceAsync(
                userId,
                new EventInferenceResult(
                    eventId,
                    EventInferenceStatus.SkippedNoTelemetry,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to persist skipped inference outcome for event {EventId}.", eventId);
        }
    }

    private async Task TryPersistSuccessAsync(
        Guid userId,
        Guid eventId,
        MlInferenceResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            await inferenceRepository.TryStoreInferenceAsync(
                userId,
                new EventInferenceResult(
                    eventId,
                    EventInferenceStatus.Succeeded,
                    response.Prediction,
                    response.SupportProbability,
                    response.Threshold,
                    response.ModelVersion,
                    response.Target,
                    null,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to persist inference success for event {EventId}.", eventId);
        }
    }

    private async Task TryPersistFailureAsync(
        Guid userId,
        Guid eventId,
        MlInferenceFailureKind? failureKind,
        CancellationToken cancellationToken)
    {
        try
        {
            await inferenceRepository.TryStoreInferenceAsync(
                userId,
                new EventInferenceResult(
                    eventId,
                    EventInferenceStatus.Failed,
                    null,
                    null,
                    null,
                    null,
                    null,
                    failureKind,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to persist inference failure for event {EventId}.", eventId);
        }
    }

    private static IReadOnlyList<MlWindowSampleRequest> MapSamples(
        IReadOnlyList<TelemetryWindowSampleRequest> samples) =>
        samples
            .Select(sample => new MlWindowSampleRequest(
                sample.Timestamp,
                sample.HeartRateBpm,
                sample.IbiMs,
                sample.SkinTemperatureCelsius,
                new MlWindowQualityRequest(
                    sample.Quality.HeartRate,
                    sample.Quality.Ibi,
                    sample.Quality.WearingState)))
            .ToArray();

    private static double ParseLookback(string? value, double fallback) =>
        double.TryParse(value, out var parsed) && parsed >= 0 ? parsed : fallback;
}