using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class WearableEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task TelemetryBatch_ShouldRequireAuthenticationAndBeIdempotent()
    {
        using var client = factory.CreateClient();
        var batch = new
        {
            batchId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            sessionId = Guid.NewGuid(),
            startedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            endedAt = DateTimeOffset.UtcNow,
            sequence = 0,
            samples = new[]
            {
                new
                {
                    timestamp = DateTimeOffset.UtcNow,
                    heartRateBpm = 72.5,
                    ibiMs = new[] { 810.0 },
                    accelerometer = new { x = 0.0, y = 0.0, z = 9.81 },
                    skinTemperatureCelsius = (double?)null,
                    ambientTemperatureCelsius = (double?)null,
                    quality = new { heartRate = "good", ibi = "good", wearingState = "onBody" }
                }
            }
        };

        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", batch)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var email = $"{Guid.NewGuid():N}@example.test";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Wearable User",
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", batch)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/v1/telemetry/batch", batch)).StatusCode.Should().Be(HttpStatusCode.OK);

        var sos = new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = (Guid?)null,
            triggeredAt = DateTimeOffset.UtcNow,
            source = "WATCH",
            reason = "Test alert"
        };

        (await client.PostAsJsonAsync("/api/v1/sos/trigger", sos)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/v1/sos/trigger", sos)).StatusCode.Should().Be(HttpStatusCode.OK);

        var cancellation = new
        {
            sos.eventId,
            sos.deviceId,
            userId = (Guid?)null,
            cancelledAt = DateTimeOffset.UtcNow,
            reason = "Cancelled on watch"
        };

        (await client.PostAsJsonAsync("/api/v1/sos/cancel", cancellation)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/v1/sos/cancel", cancellation)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SuspectedEvent_ShouldBeAcceptedThenIdempotent()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var suspected = new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = (Guid?)null,
            sessionId = Guid.NewGuid(),
            sequence = 0,
            detectedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            state = "USER_VALIDATION",
            score = 0.88,
            rulesVersion = "rules-v2",
            features = new
            {
                heartRateMean = 96.0,
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

        (await client.PostAsJsonAsync("/api/v1/events/suspected", suspected)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/v1/events/suspected", suspected)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SuspectedEvent_ShouldRejectInvalidScoreAndMissingFeatures()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var badScore = new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = (Guid?)null,
            sessionId = Guid.NewGuid(),
            sequence = 0,
            detectedAt = DateTimeOffset.UtcNow,
            state = "USER_VALIDATION",
            score = 1.5,
            rulesVersion = "rules-v2",
            features = new { validSampleRatio = 0.9, lastSampleAgeSeconds = 1L, sampleCount = 60 },
            baseline = new { sampleCount = 1L, meanHeartRate = 80.0, heartRateM2 = 0.0, updatedAtEpochMillis = 0L }
        };
        (await client.PostAsJsonAsync("/api/v1/events/suspected", badScore)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missingFeatures = new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = (Guid?)null,
            sessionId = Guid.NewGuid(),
            sequence = 0,
            detectedAt = DateTimeOffset.UtcNow,
            state = "USER_VALIDATION",
            score = 0.5,
            rulesVersion = "rules-v2",
            features = (object?)null,
            baseline = new { sampleCount = 1L, meanHeartRate = 80.0, heartRateM2 = 0.0, updatedAtEpochMillis = 0L }
        };
        (await client.PostAsJsonAsync("/api/v1/events/suspected", missingFeatures)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SuspectedEvent_ShouldRejectMismatchedUserId()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var suspected = new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = (Guid?)Guid.NewGuid(),
            sessionId = Guid.NewGuid(),
            sequence = 0,
            detectedAt = DateTimeOffset.UtcNow,
            state = "USER_VALIDATION",
            score = 0.5,
            rulesVersion = "rules-v2",
            features = new { validSampleRatio = 0.9, lastSampleAgeSeconds = 1L, sampleCount = 60 },
            baseline = new { sampleCount = 1L, meanHeartRate = 80.0, heartRateM2 = 0.0, updatedAtEpochMillis = 0L }
        };

        (await client.PostAsJsonAsync("/api/v1/events/suspected", suspected)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EventDecision_ShouldBeAcceptedThenIdempotent()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var decision = new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = (Guid?)null,
            sessionId = Guid.NewGuid(),
            sequence = 0,
            detectedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            respondedAt = DateTimeOffset.UtcNow,
            response = "SUPPORT_REQUESTED"
        };

        (await client.PostAsJsonAsync("/api/v1/events/decision", decision)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync("/api/v1/events/decision", decision)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EventDecision_ShouldRejectNonPrimaryResponse()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var decision = new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = (Guid?)null,
            sessionId = Guid.NewGuid(),
            sequence = 0,
            detectedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
            respondedAt = DateTimeOffset.UtcNow,
            response = "BREATHING_HELPED"
        };

        (await client.PostAsJsonAsync("/api/v1/events/decision", decision)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
