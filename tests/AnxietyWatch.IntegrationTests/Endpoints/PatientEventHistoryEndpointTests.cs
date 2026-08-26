using System.Net;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class PatientEventHistoryEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task UnauthenticatedRequest_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/events")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthenticatedPatientGetsOnlyTheirEmptyHistory()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/events?limit=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PatientEventResponse[]>()).Should().BeEmpty();
    }
}

public sealed record PatientEventResponse(Guid EventId, string Type, DateTimeOffset OccurredAt, string? Status);
