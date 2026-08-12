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
    public async Task SosCancel_ShouldRequireAuthenticationValidateOwnershipAndBeIdempotent()
    {
        using var client = factory.CreateClient();
        var eventId = Guid.NewGuid();
        var cancellation = new
        {
            eventId,
            deviceId = Guid.NewGuid(),
            userId = (Guid?)null,
            cancelledAt = DateTimeOffset.UtcNow,
            reason = "False alarm"
        };

        (await client.PostAsJsonAsync("/api/v1/sos/cancel", cancellation)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var first = await client.PostAsJsonAsync("/api/v1/sos/cancel", cancellation);
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await first.Content.ReadFromJsonAsync<SubmissionResponse>()).Should().Be(
            new SubmissionResponse(eventId, true, false));

        var duplicate = await client.PostAsJsonAsync("/api/v1/sos/cancel", cancellation);
        duplicate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await duplicate.Content.ReadFromJsonAsync<SubmissionResponse>()).Should().Be(
            new SubmissionResponse(eventId, false, true));

        (await client.PostAsJsonAsync("/api/v1/sos/cancel", new
        {
            eventId = Guid.Empty,
            deviceId = Guid.Empty,
            cancelledAt = default(DateTimeOffset),
            reason = new string('x', 501)
        })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/api/v1/sos/cancel", new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = Guid.NewGuid(),
            cancelledAt = DateTimeOffset.UtcNow
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Wearable User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });

        return (await registration.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private sealed record AuthResponse(string Token);
    private sealed record SubmissionResponse(Guid EventId, bool Accepted, bool Duplicate);
}
