using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class ProtectedResourceTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task FreePlan_ShouldRejectTheSixthWeeklyEpisode()
    {
        using var client = await CreateAuthenticatedClient();

        for (var index = 0; index < 5; index++)
        {
            var response = await client.PostAsJsonAsync("/api/episodes", new
            {
                intensity = index,
                symptoms = Array.Empty<string>(),
                notes = "test"
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var sixthResponse = await client.PostAsJsonAsync("/api/episodes", new
        {
            intensity = 50,
            symptoms = Array.Empty<string>(),
            notes = "test"
        });

        sixthResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FreePlan_ShouldEnforceTokenQuotaAndRestoreItAfterDeletion()
    {
        using var client = await CreateAuthenticatedClient();

        var firstResponse = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });
        var firstToken = await firstResponse.Content.ReadFromJsonAsync<TokenResponse>();
        var secondResponse = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var deleteResponse = await client.DeleteAsync($"/api/tokens/{firstToken!.Id}");
        var thirdResponse = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });

        deleteResponse.IsSuccessStatusCode.Should().BeTrue();
        thirdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Protected Resource User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var registration = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration!.Token);
        return client;
    }

    private sealed record AuthResponse(string Token);
    private sealed record TokenResponse(string Id);
}
