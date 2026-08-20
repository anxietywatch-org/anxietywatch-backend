using AnxietyWatch.Application.Abstractions.MlInference;
using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.Infrastructure.Wearables;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public abstract class SuspectedEventInferenceFlowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    protected IWearableSyncRepository SyncRepository = null!;
    protected IEventInferenceRepository InferenceRepository = null!;
    protected ISuspectedEventInferenceService Service = null!;
    protected FakeMlInferenceClient MlClient = new();

    protected void BuildService(int lookbackSeconds = 60)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ml:Inference:TelemetryLookbackSeconds"] = lookbackSeconds.ToString()
            })
            .Build();
        Service = new SuspectedEventInferenceService(
            NullLogger<SuspectedEventInferenceService>.Instance,
            SyncRepository,
            InferenceRepository,
            MlClient,
            configuration);
    }

    protected void BuildServiceFromLookback(string? lookbackValue)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ml:Inference:TelemetryLookbackSeconds"] = lookbackValue
            })
            .Build();
        Service = new SuspectedEventInferenceService(
            NullLogger<SuspectedEventInferenceService>.Instance,
            SyncRepository,
            InferenceRepository,
            MlClient,
            configuration);
    }

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
        double? skinTemperatureCelsius = 35.4,
        string heartRateQuality = "good",
        string ibiQuality = "good",
        string wearingState = "onBody") =>
        new(timestamp, heartRateBpm, ibiMs ?? [], null, skinTemperatureCelsius, null,
            new TelemetryQualityRequest(heartRateQuality, ibiQuality, wearingState));

    private static SuspectedEventRequest SuspectedEvent(
        Guid eventId,
        Guid deviceId,
        Guid sessionId,
        DateTimeOffset detectedAt) =>
        new(
            eventId,
            deviceId,
            null,
            sessionId,
            0,
            detectedAt,
            "USER_VALIDATION",
            0.5,
            "rules-v2",
            new SuspectedEventFeaturesRequest(
                null, null, null, null, null, null, null, null, 1, 0, 10),
            new SuspectedEventBaselineRequest(240, 82, 310, 0));

    private static async Task StoreBatchAsync(
        IWearableSyncRepository repository,
        Guid userId,
        TelemetryBatchRequest batch) =>
        (await repository.TryStoreTelemetryAsync(userId, batch)).Should().BeTrue();

    [Fact]
    public async Task A_NewEvent_StoresTelemetryAndCallsMlExactlyOnce()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var detectedAt = T0;
        var evt = SuspectedEvent(eventId, deviceId, sessionId, detectedAt);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-30), T0, 0,
            Sample(T0.AddSeconds(-30), 88, [820], 35.4),
            Sample(T0)));
        MlClient.EnqueueSuccess(prediction: 0, supportProbability: 0.2, threshold: 0.3);

        await Service.RunInferenceAsync(userId, evt);

        MlClient.CallCount.Should().Be(1);
        var request = MlClient.Requests.Should().ContainSingle().Subject;
        request.EventId.Should().Be(eventId);
        request.DeviceId.Should().Be(deviceId);
        request.SessionId.Should().Be(sessionId);
        request.DetectedAt.Should().Be(detectedAt);
        request.Samples.Select(sample => sample.Timestamp).Should().Equal(T0.AddSeconds(-30), T0);

        var inference = (await InferenceRepository.GetInferenceAsync(userId, eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.Succeeded);
        inference.Prediction.Should().Be(0);
        inference.SupportProbability.Should().Be(0.2);
        inference.Threshold.Should().Be(0.3);
        inference.ModelVersion.Should().Be("v0.1.0");
        inference.Target.Should().Be("target_support_requested");
        inference.FailureKind.Should().BeNull();
    }

    [Fact]
    public async Task C_WrongUsersTelemetry_IsNeverIncluded()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await StoreBatchAsync(SyncRepository, firstUser, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-30), T0, 0,
            Sample(T0.AddSeconds(-30))));

        var evt = SuspectedEvent(Guid.NewGuid(), deviceId, sessionId, T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(secondUser, evt)).Should().BeTrue();
        MlClient.EnqueueSuccess();

        await Service.RunInferenceAsync(secondUser, evt);

        MlClient.CallCount.Should().Be(0);
        var inference = (await InferenceRepository.GetInferenceAsync(secondUser, evt.EventId))!;
        inference.Status.Should().Be(EventInferenceStatus.SkippedNoTelemetry);
    }

    [Fact]
    public async Task D_WrongDeviceAndSessionTelemetry_IsNeverIncluded()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-30), T0, 0,
            Sample(T0.AddSeconds(-30))));

        var evt = SuspectedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();

        await Service.RunInferenceAsync(userId, evt);

        MlClient.CallCount.Should().Be(0);
        var inference = (await InferenceRepository.GetInferenceAsync(userId, evt.EventId))!;
        inference.Status.Should().Be(EventInferenceStatus.SkippedNoTelemetry);
    }

    [Fact]
    public async Task E_MultipleBatches_CombinedSamplesSentToMl()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-60), T0.AddSeconds(-20), 0,
            Sample(T0.AddSeconds(-50), 80, [810, 820], 35.0),
            Sample(T0.AddSeconds(-40), 82, [800], 35.1)));
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-30), T0, 1,
            Sample(T0.AddSeconds(-30), 84, [790], 35.2),
            Sample(T0)));
        var evt = SuspectedEvent(eventId, deviceId, sessionId, T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        MlClient.EnqueueSuccess();

        await Service.RunInferenceAsync(userId, evt);

        var request = MlClient.Requests.Should().ContainSingle().Subject;
        request.Samples.Select(sample => sample.Timestamp).Should().Equal(
            T0.AddSeconds(-50), T0.AddSeconds(-40), T0.AddSeconds(-30), T0);
        request.Samples[0].HeartRateBpm.Should().Be(80);
        request.Samples[0].IbiMs.Should().Equal(810, 820);
        request.Samples[0].SkinTemperatureCelsius.Should().Be(35.0);
    }

    [Fact]
    public async Task F_ExactLookbackBoundaries_MapCorrectly()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var detectedAt = T0;
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-61), T0, 0,
            Sample(T0.AddSeconds(-61)),
            Sample(T0.AddSeconds(-60)),
            Sample(T0),
            Sample(T0.AddSeconds(1))));
        var evt = SuspectedEvent(eventId, deviceId, sessionId, detectedAt);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        MlClient.EnqueueSuccess();

        await Service.RunInferenceAsync(userId, evt);

        var request = MlClient.Requests.Should().ContainSingle().Subject;
        request.Samples.Select(sample => sample.Timestamp).Should().Equal(T0.AddSeconds(-60), T0);
    }

    [Fact]
    public async Task RawMapping_ExactValuesWithoutExtras()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var detectedAt = T0;
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-10), T0, 0,
            new TelemetrySampleRequest(
                T0.AddSeconds(-10),
                96.5,
                [810.5, 820.25],
                null,
                35.6,
                null,
                new TelemetryQualityRequest("fair", "good", "offBody"))));
        var evt = SuspectedEvent(eventId, deviceId, sessionId, detectedAt);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        MlClient.EnqueueSuccess();

        await Service.RunInferenceAsync(userId, evt);

        var request = MlClient.Requests.Should().ContainSingle().Subject;
        var sample = request.Samples.Should().ContainSingle().Subject;
        sample.Timestamp.Should().Be(T0.AddSeconds(-10));
        sample.HeartRateBpm.Should().Be(96.5);
        sample.IbiMs.Should().Equal(810.5, 820.25);
        sample.SkinTemperatureCelsius.Should().Be(35.6);
        sample.Quality.HeartRate.Should().Be("fair");
        sample.Quality.Ibi.Should().Be("good");
        sample.Quality.WearingState.Should().Be("offBody");
        request.Samples.Should().HaveCount(1);
    }

    [Fact]
    public async Task ZeroTelemetry_SkippedNoTelemetryWithoutCallingMl()
    {
        var userId = Guid.NewGuid();
        var evt = SuspectedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();

        await Service.RunInferenceAsync(userId, evt);

        MlClient.CallCount.Should().Be(0);
        var inference = (await InferenceRepository.GetInferenceAsync(userId, evt.EventId))!;
        inference.Status.Should().Be(EventInferenceStatus.SkippedNoTelemetry);
        inference.Prediction.Should().BeNull();
        inference.FailureKind.Should().BeNull();
    }

    [Fact]
    public async Task SuccessPredictionOne_PreservedWithoutProductSideEffect()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-10), T0, 0,
            Sample(T0.AddSeconds(-10))));
        var evt = SuspectedEvent(eventId, deviceId, sessionId, T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        MlClient.EnqueueSuccess(prediction: 1, supportProbability: 0.95, threshold: 0.3);

        await Service.RunInferenceAsync(userId, evt);

        var inference = (await InferenceRepository.GetInferenceAsync(userId, eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.Succeeded);
        inference.Prediction.Should().Be(1);
        inference.SupportProbability.Should().Be(0.95);
        inference.ModelVersion.Should().Be("v0.1.0");
        inference.Target.Should().Be("target_support_requested");
    }

    [Theory]
    [InlineData(MlInferenceFailureKind.Unauthorized)]
    [InlineData(MlInferenceFailureKind.Validation)]
    [InlineData(MlInferenceFailureKind.Transient)]
    [InlineData(MlInferenceFailureKind.Unexpected)]
    [InlineData(MlInferenceFailureKind.Configuration)]
    public async Task FailureKinds_PersistFailedWithoutThrowing(MlInferenceFailureKind kind)
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-10), T0, 0,
            Sample(T0.AddSeconds(-10))));
        var evt = SuspectedEvent(eventId, deviceId, sessionId, T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        MlClient.EnqueueFailure(kind);

        await Service.RunInferenceAsync(userId, evt);

        var inference = (await InferenceRepository.GetInferenceAsync(userId, eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.Failed);
        inference.FailureKind.Should().Be(kind);
        inference.Prediction.Should().BeNull();
    }

    [Fact]
    public async Task UnexpectedClientException_EventSurvivesWithGenericFailure()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-10), T0, 0,
            Sample(T0.AddSeconds(-10))));
        var evt = SuspectedEvent(eventId, deviceId, sessionId, T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        MlClient.EnqueueThrow(new InvalidOperationException("boom"));

        await Service.RunInferenceAsync(userId, evt);

        var inference = (await InferenceRepository.GetInferenceAsync(userId, eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.Failed);
        inference.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
    }

[Fact]
    public async Task PersistenceIdempotency_SingleRecordForEventId()
    {
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var result = new EventInferenceResult(
            eventId,
            EventInferenceStatus.Succeeded,
            1,
            0.95,
            0.3,
            "v0.1.0",
            "target_support_requested",
            null,
            T0);

        (await InferenceRepository.TryStoreInferenceAsync(userId, result)).Should().BeTrue();
        (await InferenceRepository.TryStoreInferenceAsync(userId, result)).Should().BeFalse();

        var stored = (await InferenceRepository.GetInferenceAsync(userId, eventId))!;
        stored.Status.Should().Be(EventInferenceStatus.Succeeded);
        stored.Prediction.Should().Be(1);
    }

    [Fact]
    public async Task CrossUserReadIsolation_OtherUsersCannotReadEventId()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var result = new EventInferenceResult(
            eventId,
            EventInferenceStatus.Succeeded,
            0,
            0.2,
            0.3,
            "v0.1.0",
            "target_support_requested",
            null,
            T0);

        (await InferenceRepository.TryStoreInferenceAsync(firstUser, result)).Should().BeTrue();

        (await InferenceRepository.GetInferenceAsync(firstUser, eventId))!.Should().BeEquivalentTo(result);
        (await InferenceRepository.GetInferenceAsync(secondUser, eventId)).Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("not-a-number")]
    [InlineData("")]
    public async Task InvalidLookbackConfiguration_FallsBackToSixtySeconds(string value)
    {
        BuildServiceFromLookback(value);
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-30), T0, 0,
            Sample(T0.AddSeconds(-30))));
        var evt = SuspectedEvent(Guid.NewGuid(), deviceId, sessionId, T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        MlClient.EnqueueSuccess();

        await Service.RunInferenceAsync(userId, evt);

        MlClient.CallCount.Should().Be(1);
        var request = MlClient.Requests.Should().ContainSingle().Subject;
        request.Samples.Select(sample => sample.Timestamp).Should().Equal(T0.AddSeconds(-30));
    }

    [Fact]
    public async Task AttemptedAt_IsCapturedAtAttemptStartNotCompletion()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await StoreBatchAsync(SyncRepository, userId, Batch(
            Guid.NewGuid(), deviceId, sessionId, T0.AddSeconds(-10), T0, 0,
            Sample(T0.AddSeconds(-10))));
        var evt = SuspectedEvent(eventId, deviceId, sessionId, T0);
        (await SyncRepository.TryStoreSuspectedEventAsync(userId, evt)).Should().BeTrue();
        MlClient.EnqueueAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300));
            return MlInferenceResult.Success(new MlInferenceResponse(
                0, 0.1, 0.3, "v0.1.0", "target_support_requested"));
        });

        var started = DateTimeOffset.UtcNow;
        await Service.RunInferenceAsync(userId, evt);
        var finished = DateTimeOffset.UtcNow;

        var inference = (await InferenceRepository.GetInferenceAsync(userId, eventId))!;
        inference.AttemptedAt.Should().BeOnOrAfter(started);
        inference.AttemptedAt.Should().BeOnOrBefore(finished - TimeSpan.FromMilliseconds(150));
    }
}