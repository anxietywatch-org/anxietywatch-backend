using System.Net;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.MlInference;
using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class SuspectedEventInferenceEndpointTests : IClassFixture<InferenceTestFactory>, IAsyncLifetime
{
    private readonly InferenceTestFactory factory;

    public SuspectedEventInferenceEndpointTests(InferenceTestFactory factory) => this.factory = factory;

    public Task InitializeAsync()
    {
        factory.MlClient.Reset();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object TelemetryBatch(
        Guid deviceId,
        Guid sessionId,
        DateTimeOffset detectedAt) =>
        new
        {
            batchId = Guid.NewGuid(),
            deviceId,
            sessionId,
            startedAt = detectedAt.AddSeconds(-30),
            endedAt = detectedAt,
            sequence = 0,
            samples = new[]
            {
                new
                {
                    timestamp = detectedAt.AddSeconds(-30),
                    heartRateBpm = 96.5,
                    ibiMs = new[] { 810.0, 820.0 },
                    accelerometer = new { x = 0.0, y = 0.0, z = 9.81 },
                    skinTemperatureCelsius = (double?)35.6,
                    ambientTemperatureCelsius = (double?)null,
                    quality = new { heartRate = "good", ibi = "good", wearingState = "onBody" }
                }
            }
        };

    private static object SuspectedEvent(
        Guid eventId,
        Guid deviceId,
        Guid sessionId,
        DateTimeOffset detectedAt) =>
        new
        {
            eventId,
            deviceId,
            userId = (Guid?)null,
            sessionId,
            sequence = 0,
            detectedAt,
            state = "USER_VALIDATION",
            score = 0.5,
            rulesVersion = "rules-v2",
            features = new
            {
                heartRateMean = (double?)96.0,
                heartRateMax = (double?)108.0,
                heartRateSlopeBpmPerMinute = (double?)1.2,
                heartRateDeltaFromBaseline = (double?)12.0,
                rmssdMillis = (double?)21.0,
                sdnnMillis = (double?)30.0,
                movementMagnitudeMean = (double?)0.05,
                movementVariance = (double?)0.0004,
                validSampleRatio = 0.95,
                lastSampleAgeSeconds = 5L,
                sampleCount = 60
            },
            baseline = new
            {
                sampleCount = 240L,
                meanHeartRate = 82.0,
                heartRateM2 = 310.0,
                updatedAtEpochMillis = 1780000000000L
            }
        };

    [Fact]
    public async Task NewEvent_IsAccepted_TelemetryRetrieved_MlCalledOnce()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var detectedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        factory.MlClient.EnqueueSuccess(prediction: 0, supportProbability: 0.2, threshold: 0.3);

        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", TelemetryBatch(deviceId, sessionId, detectedAt)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await client.PostAsJsonAsync("/api/v1/events/suspected", SuspectedEvent(eventId, deviceId, sessionId, detectedAt));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        factory.MlClient.CallCount.Should().Be(1);
        var request = factory.MlClient.Requests.Should().ContainSingle().Subject;
        request.EventId.Should().Be(eventId);
        request.DeviceId.Should().Be(deviceId);
        request.SessionId.Should().Be(sessionId);
        request.DetectedAt.Should().Be(detectedAt);

        var inference = (await factory.Inferences.GetInferenceAsync(eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.Succeeded);
        inference.Prediction.Should().Be(0);
        inference.ModelVersion.Should().Be("v0.1.0");
        inference.Target.Should().Be("target_support_requested");
    }

    [Fact]
    public async Task DuplicateEvent_ReturnsExistingResponse_NoAdditionalMlCall()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var detectedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        factory.MlClient.EnqueueSuccess();

        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", TelemetryBatch(deviceId, sessionId, detectedAt)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var suspected = SuspectedEvent(eventId, deviceId, sessionId, detectedAt);
        var first = await client.PostAsJsonAsync("/api/v1/events/suspected", suspected);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstBody = await first.Content.ReadFromJsonAsync<EventSubmissionResponse>();
        firstBody!.Accepted.Should().BeTrue();
        firstBody.Duplicate.Should().BeFalse();

        var second = await client.PostAsJsonAsync("/api/v1/events/suspected", suspected);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<EventSubmissionResponse>();
        secondBody!.Accepted.Should().BeFalse();
        secondBody.Duplicate.Should().BeTrue();

        factory.MlClient.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task PredictionOne_PreservedWithoutSosOrCaregiverDispatch()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var detectedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        factory.MlClient.EnqueueSuccess(prediction: 1, supportProbability: 0.95);

        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", TelemetryBatch(deviceId, sessionId, detectedAt)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await client.PostAsJsonAsync("/api/v1/events/suspected", SuspectedEvent(eventId, deviceId, sessionId, detectedAt));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        factory.PushNotifier.Messages.Should().BeEmpty();
        var inference = (await factory.Inferences.GetInferenceAsync(eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.Succeeded);
        inference.Prediction.Should().Be(1);
    }

    [Theory]
    [InlineData(MlInferenceFailureKind.Unauthorized)]
    [InlineData(MlInferenceFailureKind.Validation)]
    [InlineData(MlInferenceFailureKind.Transient)]
    [InlineData(MlInferenceFailureKind.Unexpected)]
    [InlineData(MlInferenceFailureKind.Configuration)]
    public async Task MlFailure_EventStillAcceptedAndFailedPersisted(MlInferenceFailureKind kind)
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var detectedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        factory.MlClient.EnqueueFailure(kind);

        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", TelemetryBatch(deviceId, sessionId, detectedAt)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await client.PostAsJsonAsync("/api/v1/events/suspected", SuspectedEvent(eventId, deviceId, sessionId, detectedAt));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var inference = (await factory.Inferences.GetInferenceAsync(eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.Failed);
        inference.FailureKind.Should().Be(kind);
    }

    [Fact]
    public async Task UnexpectedClientException_EventStillAcceptedWithGenericFailure()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var detectedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        factory.MlClient.EnqueueThrow(new InvalidOperationException("boom"));

        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", TelemetryBatch(deviceId, sessionId, detectedAt)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await client.PostAsJsonAsync("/api/v1/events/suspected", SuspectedEvent(eventId, deviceId, sessionId, detectedAt));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var inference = (await factory.Inferences.GetInferenceAsync(eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.Failed);
        inference.FailureKind.Should().Be(MlInferenceFailureKind.Unexpected);
    }

    [Fact]
    public async Task ZeroTelemetry_EventAccepted_MlNotCalled_SkippedPersisted()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var eventId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/v1/events/suspected", SuspectedEvent(
            eventId, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-5)));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        factory.MlClient.CallCount.Should().Be(0);
        var inference = (await factory.Inferences.GetInferenceAsync(eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.SkippedNoTelemetry);
    }

    [Fact]
    public async Task WrongUsersTelemetry_IsNeverIncluded()
    {
        using var firstClient = await factory.CreateAuthenticatedClientAsync();
        var deviceId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var detectedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        (await firstClient.PostAsJsonAsync("/api/v1/telemetry/batch", TelemetryBatch(deviceId, sessionId, detectedAt)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var secondClient = await factory.CreateAuthenticatedClientAsync();
        var eventId = Guid.NewGuid();
        var response = await secondClient.PostAsJsonAsync("/api/v1/events/suspected", SuspectedEvent(
            eventId, deviceId, sessionId, detectedAt));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        factory.MlClient.CallCount.Should().Be(0);
        var inference = (await factory.Inferences.GetInferenceAsync(eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.SkippedNoTelemetry);
    }

    [Fact]
    public async Task WrongDeviceTelemetry_IsNeverIncluded()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var detectedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", TelemetryBatch(Guid.NewGuid(), Guid.NewGuid(), detectedAt)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var eventId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/v1/events/suspected", SuspectedEvent(
            eventId, Guid.NewGuid(), Guid.NewGuid(), detectedAt));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        factory.MlClient.CallCount.Should().Be(0);
        var inference = (await factory.Inferences.GetInferenceAsync(eventId))!;
        inference.Status.Should().Be(EventInferenceStatus.SkippedNoTelemetry);
    }

    private sealed record EventSubmissionResponse(Guid EventId, bool Accepted, bool Duplicate);
}