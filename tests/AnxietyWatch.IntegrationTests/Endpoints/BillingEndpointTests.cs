using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class BillingEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task SimulatePayment_ShouldPersistPlanAndExposeSummary()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        var payment = await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "individual",
            billingCycle = "monthly"
        });

        payment.StatusCode.Should().Be(HttpStatusCode.Created);
        (await payment.Content.ReadAsStringAsync()).Should().Contain("\"simulated\":true");

        var session = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");
        session!.User.PlanId.Should().Be("individual");

        var summary = await client.GetAsync("/api/billing/summary");
        summary.IsSuccessStatusCode.Should().BeTrue();
        (await summary.Content.ReadAsStringAsync()).Should().Contain("individual");
        (await client.GetAsync("/api/billing/transactions")).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task TokenQuota_ShouldBeAvailableWithoutChangingLegacyTokenList()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        var quota = await client.GetAsync("/api/tokens/quota");
        quota.StatusCode.Should().Be(HttpStatusCode.OK);
        (await quota.Content.ReadAsStringAsync()).Should().Contain("remaining");
        (await client.GetAsync("/api/tokens")).IsSuccessStatusCode.Should().BeTrue();
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Billing MVP User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private sealed record AuthResponse(string Token, UserResponse User);
    private sealed record UserResponse(string Id, string FullName, string Email, string PlanId, bool EmailVerified);
}
