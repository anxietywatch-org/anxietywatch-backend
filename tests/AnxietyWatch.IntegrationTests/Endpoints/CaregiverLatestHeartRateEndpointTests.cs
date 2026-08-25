using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CaregiverLatestHeartRateEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task LinkedCaregiverGetsLatestValidHeartRateWithoutSensitiveFields()
    {
        var (patient, patientId) = await CreateUserAsync("Patient");
        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        await AddAcceptedRelationshipAsync(patientId, caregiverId);
        var measuredAt = DateTimeOffset.UtcNow.AddSeconds(-18);
        var batch = Batch(patientId, measuredAt, 82);

        (await patient.PostAsJsonAsync("/api/v1/telemetry/batch", batch)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var response = await caregiver.GetAsync($"/api/caregiver/patients/{patientId}/heart-rate/latest");
        var json = await response.Content.ReadAsStringAsync();
        var result = await response.Content.ReadFromJsonAsync<CaregiverLatestHeartRateResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        result!.HeartRateBpm.Should().Be(82);
        result.MeasuredAt.Should().Be(measuredAt);
        result.AgeSeconds.Should().BeInRange(17, 20);
        result.Quality.Should().Be("good");
        json.Should().NotContain("batchId").And.NotContain("deviceId").And.NotContain("sessionId")
            .And.NotContain("userId").And.NotContain("ibi").And.NotContain("accelerometer")
            .And.NotContain("temperature");
    }

    [Fact]
    public async Task UnauthorizedCaregiverAndEmptyPatientAreHandledSafely()
    {
        using var anonymous = factory.CreateClient();
        var (patient, patientId) = await CreateUserAsync("Patient");
        var (otherCaregiver, _) = await CreateUserAsync("Other Caregiver");

        (await anonymous.GetAsync($"/api/caregiver/patients/{patientId}/heart-rate/latest"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await otherCaregiver.GetAsync($"/api/caregiver/patients/{patientId}/heart-rate/latest"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        await AddAcceptedRelationshipAsync(patientId, caregiverId);
        (await caregiver.GetAsync($"/api/caregiver/patients/{patientId}/heart-rate/latest"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        _ = patient;
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateUserAsync(string name)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = name,
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return (client, Guid.Parse(auth.User.Id));
    }

    private async Task AddAcceptedRelationshipAsync(Guid patientId, Guid caregiverId)
    {
        var token = new LinkToken(Guid.NewGuid(), patientId, $"AW-{Guid.NewGuid():N}"[..15], "family_member", DateTimeOffset.UtcNow.AddDays(30));
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>();
        (await tokens.TryAddAsync(token, 10)).Should().BeTrue();
        (await tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, DateTimeOffset.UtcNow)).Should().BeTrue();
    }

    private static TelemetryBatchRequest Batch(Guid patientId, DateTimeOffset measuredAt, double? heartRate) =>
        new(Guid.NewGuid(), Guid.NewGuid(), patientId, Guid.NewGuid(), measuredAt, measuredAt, 1,
            [new(measuredAt, heartRate, [], null, null, null, new("good", "unknown", "onBody"))]);

    private sealed record AuthResponse(string Token, UserResponse User);
    private sealed record UserResponse(string Id);
}
