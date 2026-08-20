using AnxietyWatch.Application.Features.Wearables;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public abstract class TelemetryWindowQueryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    protected IWearableSyncRepository Repository = null!;

    private static TelemetryBatchRequest Batch(
        Guid batchId,
        Guid deviceId,
        Guid sessionId,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        long sequence,
        params TelemetrySampleRequest[] samples) =>
        new(batchId, deviceId, null, sessionId, startedAt, endedAt, sequence, samples);

    private static TelemetrySampleRequest Sample(
        DateTimeOffset timestamp,
        double? heartRateBpm = 72,
        IReadOnlyList<double>? ibiMs = null,
        double? skinTemperatureCelsius = 35.4) =>
        new(timestamp, heartRateBpm, ibiMs ?? [], null, skinTemperatureCelsius, null,
            new TelemetryQualityRequest("good", "good", "onBody"));

    [Fact]
    public async Task A_SingleBatch_ReturnsOnlySamplesWithinWindow()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var batch = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-30), T0.AddSeconds(90), 0,
            Sample(T0.AddSeconds(-30)),
            Sample(T0),
            Sample(T0.AddSeconds(30)),
            Sample(T0.AddSeconds(60)),
            Sample(T0.AddSeconds(90)));
        await Repository.TryStoreTelemetryAsync(userId, batch);

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        result.Samples.Select(sample => sample.Timestamp).Should().Equal(
            T0, T0.AddSeconds(30), T0.AddSeconds(60));
    }

    [Fact]
    public async Task B_MultipleBatches_AreCombinedAndSortedAscending()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var first = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(10)),
            Sample(T0.AddSeconds(20)));
        var second = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(20), T0.AddSeconds(50), 1,
            Sample(T0.AddSeconds(30)),
            Sample(T0.AddSeconds(40)));
        await Repository.TryStoreTelemetryAsync(userId, first);
        await Repository.TryStoreTelemetryAsync(userId, second);

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        result.Samples.Select(sample => sample.Timestamp).Should().Equal(
            T0.AddSeconds(10), T0.AddSeconds(20), T0.AddSeconds(30), T0.AddSeconds(40));
    }

    [Fact]
    public async Task C_PartiallyOverlappingBatch_ReturnsOnlyOverlappingSamples()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var batch = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-60), T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(-60)),
            Sample(T0.AddSeconds(-30)),
            Sample(T0),
            Sample(T0.AddSeconds(30)));
        await Repository.TryStoreTelemetryAsync(userId, batch);

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        result.Samples.Select(sample => sample.Timestamp).Should().Equal(
            T0, T0.AddSeconds(30));
    }

    [Fact]
    public async Task D_ExactBoundarySamples_AreIncluded()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var batch = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(60), 0,
            Sample(T0),
            Sample(T0.AddSeconds(60)));
        await Repository.TryStoreTelemetryAsync(userId, batch);

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        result.Samples.Select(sample => sample.Timestamp).Should().Equal(T0, T0.AddSeconds(60));
    }

    [Fact]
    public async Task E_OutOfOrderSamples_AreReturnedSortedAscending()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var batch = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(40), 0,
            Sample(T0.AddSeconds(40)),
            Sample(T0.AddSeconds(10)),
            Sample(T0.AddSeconds(30)));
        await Repository.TryStoreTelemetryAsync(userId, batch);

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        result.Samples.Select(sample => sample.Timestamp).Should().Equal(
            T0.AddSeconds(10), T0.AddSeconds(30), T0.AddSeconds(40));
    }

    [Fact]
    public async Task F_OtherSessionsDevicesAndUsers_AreExcluded()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var expected = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(10)));
        var wrongSession = Batch(
            Guid.NewGuid(), deviceId, Guid.NewGuid(), T0, T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(20)));
        var wrongDevice = Batch(
            Guid.NewGuid(), Guid.NewGuid(), sessionId, T0, T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(30)));
        var wrongUser = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(40)));
        await Repository.TryStoreTelemetryAsync(userId, expected);
        await Repository.TryStoreTelemetryAsync(userId, wrongSession);
        await Repository.TryStoreTelemetryAsync(userId, wrongDevice);
        await Repository.TryStoreTelemetryAsync(Guid.NewGuid(), wrongUser);

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        result.Samples.Select(sample => sample.Timestamp).Should().Equal(T0.AddSeconds(10));
    }

    [Fact]
    public async Task G_EmptyStore_ReturnsEmptyResultWithoutError()
    {
        var userId = Guid.NewGuid();

        var result = await Repository.GetTelemetryWindowAsync(
            userId, Guid.NewGuid(), Guid.NewGuid(), T0, T0.AddSeconds(60));

        result.Samples.Should().BeEmpty();
        result.Duration.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task H_NullAndEmptyValues_ArePreserved()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var batch = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(30), 0,
            new TelemetrySampleRequest(
                T0.AddSeconds(10),
                null,
                [],
                null,
                null,
                null,
                new TelemetryQualityRequest("poor", "fair", "unknown")));
        await Repository.TryStoreTelemetryAsync(userId, batch);

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        var sample = result.Samples.Should().ContainSingle().Subject;
        sample.HeartRateBpm.Should().BeNull();
        sample.IbiMs.Should().BeEmpty();
        sample.SkinTemperatureCelsius.Should().BeNull();
        sample.Quality.Should().BeEquivalentTo(new TelemetryQualityRequest("poor", "fair", "unknown"));
    }

    [Fact]
    public async Task I_DuplicateBatchReplay_ProducesNoDuplicateSamples()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var batch = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(10)),
            Sample(T0.AddSeconds(20)));
        (await Repository.TryStoreTelemetryAsync(userId, batch)).Should().BeTrue();
        (await Repository.TryStoreTelemetryAsync(userId, batch)).Should().BeFalse();

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        result.Samples.Select(sample => sample.Timestamp).Should().Equal(
            T0.AddSeconds(10), T0.AddSeconds(20));
    }

    [Fact]
    public async Task J_BatchesFullyOutsideWindow_AreExcluded()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var before = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-120), T0.AddSeconds(-1), 0,
            Sample(T0.AddSeconds(-60)));
        var after = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(61), T0.AddSeconds(120), 1,
            Sample(T0.AddSeconds(90)));
        await Repository.TryStoreTelemetryAsync(userId, before);
        await Repository.TryStoreTelemetryAsync(userId, after);

        var result = await Repository.GetTelemetryWindowAsync(userId, deviceId, sessionId, T0, T0.AddSeconds(60));

        result.Samples.Should().BeEmpty();
    }

    [Fact]
    public async Task K_OtherUsersWithIdenticalIdentities_AreIsolated()
    {
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var firstBatch = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(10)));
        var secondBatch = Batch(
            Guid.NewGuid(), deviceId, sessionId, T0, T0.AddSeconds(30), 0,
            Sample(T0.AddSeconds(20)));
        await Repository.TryStoreTelemetryAsync(firstUser, firstBatch);
        await Repository.TryStoreTelemetryAsync(secondUser, secondBatch);

        var firstResult = await Repository.GetTelemetryWindowAsync(firstUser, deviceId, sessionId, T0, T0.AddSeconds(60));
        var secondResult = await Repository.GetTelemetryWindowAsync(secondUser, deviceId, sessionId, T0, T0.AddSeconds(60));

        firstResult.Samples.Select(sample => sample.Timestamp).Should().Equal(T0.AddSeconds(10));
        secondResult.Samples.Select(sample => sample.Timestamp).Should().Equal(T0.AddSeconds(20));
    }
}