namespace AnxietyWatch.Application.Features.Wearables;

public sealed record TelemetryWindowSampleRequest(
    DateTimeOffset Timestamp,
    double? HeartRateBpm,
    IReadOnlyList<double> IbiMs,
    double? SkinTemperatureCelsius,
    TelemetryQualityRequest Quality);

public sealed record TelemetryWindowResult(
    IReadOnlyList<TelemetryWindowSampleRequest> Samples,
    TimeSpan Duration);

public static class TelemetryWindowSelector
{
    public static TelemetryWindowResult Select(
        IEnumerable<TelemetryBatchRequest> batches,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var samples = batches
            .Where(batch => batch.EndedAt >= windowStart && batch.StartedAt <= windowEnd)
            .SelectMany(batch => batch.Samples)
            .Where(sample => sample.Timestamp >= windowStart && sample.Timestamp <= windowEnd)
            .Select(sample => new TelemetryWindowSampleRequest(
                sample.Timestamp,
                sample.HeartRateBpm,
                sample.IbiMs,
                sample.SkinTemperatureCelsius,
                sample.Quality))
            .OrderBy(sample => sample.Timestamp)
            .ToArray();

        return new TelemetryWindowResult(samples, windowEnd - windowStart);
    }
}