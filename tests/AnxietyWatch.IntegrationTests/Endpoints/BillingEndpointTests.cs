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
        (await quota.Content.ReadFromJsonAsync<TokenQuotaResponse>())!.Remaining.Should().Be(1);
        (await client.GetAsync("/api/tokens")).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task DowngradeToFree_IndividualToFree_ReturnsChangedTrue()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        // First upgrade to individual
        var payment = await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "individual",
            billingCycle = "monthly"
        });
        payment.StatusCode.Should().Be(HttpStatusCode.Created);

        var session = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");
        session!.User.PlanId.Should().Be("individual");

        // Now downgrade to free
        var downgrade = await client.PostAsync("/api/billing/downgrade-to-free", null);
        downgrade.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await downgrade.Content.ReadFromJsonAsync<DowngradeToFreeResponse>();
        body!.PlanId.Should().Be("free");
        body.PreviousPlanId.Should().Be("individual");
        body.Changed.Should().BeTrue();
        body.DowngradedAt.Should().NotBeNull();

        // Verify session reflects the change
        var sessionAfter = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");
        sessionAfter!.User.PlanId.Should().Be("free");
    }

    [Fact]
    public async Task DowngradeToFree_FamilyToFree_ReturnsChangedTrue()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        var payment = await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "family",
            billingCycle = "monthly"
        });
        payment.StatusCode.Should().Be(HttpStatusCode.Created);

        var downgrade = await client.PostAsync("/api/billing/downgrade-to-free", null);
        downgrade.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await downgrade.Content.ReadFromJsonAsync<DowngradeToFreeResponse>();
        body!.PlanId.Should().Be("free");
        body.PreviousPlanId.Should().Be("family");
        body.Changed.Should().BeTrue();
    }

    [Fact]
    public async Task DowngradeToFree_ProfessionalToFree_ReturnsChangedTrue()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        var payment = await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "professional",
            billingCycle = "monthly"
        });
        payment.StatusCode.Should().Be(HttpStatusCode.Created);

        var downgrade = await client.PostAsync("/api/billing/downgrade-to-free", null);
        downgrade.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await downgrade.Content.ReadFromJsonAsync<DowngradeToFreeResponse>();
        body!.PlanId.Should().Be("free");
        body.PreviousPlanId.Should().Be("professional");
        body.Changed.Should().BeTrue();
    }

    [Fact]
    public async Task DowngradeToFree_AlreadyFree_ReturnsChangedFalse()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        var downgrade = await client.PostAsync("/api/billing/downgrade-to-free", null);
        downgrade.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await downgrade.Content.ReadFromJsonAsync<DowngradeToFreeResponse>();
        body!.PlanId.Should().Be("free");
        body.PreviousPlanId.Should().Be("free");
        body.Changed.Should().BeFalse();
        body.DowngradedAt.Should().BeNull();
    }

    [Fact]
    public async Task DowngradeToFree_IdempotentRetry_ReturnsChangedFalseOnSecondCall()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        // Upgrade first
        await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "individual",
            billingCycle = "monthly"
        });

        // First downgrade
        var downgrade1 = await client.PostAsync("/api/billing/downgrade-to-free", null);
        downgrade1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await downgrade1.Content.ReadFromJsonAsync<DowngradeToFreeResponse>();
        body1!.Changed.Should().BeTrue();

        // Second downgrade (idempotent retry)
        var downgrade2 = await client.PostAsync("/api/billing/downgrade-to-free", null);
        downgrade2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await downgrade2.Content.ReadFromJsonAsync<DowngradeToFreeResponse>();
        body2!.Changed.Should().BeFalse();
        body2.DowngradedAt.Should().BeNull();

        // Only the original payment transaction exists (no synthetic downgrade records)
        var transactions = await client.GetFromJsonAsync<SimulatedPaymentResponse[]>("/api/billing/transactions");
        transactions!.Length.Should().Be(1);
    }

    [Fact]
    public async Task DowngradeToFree_ConcurrentRequests_PlanBecomesFreeOnce()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        // Upgrade first
        await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "individual",
            billingCycle = "monthly"
        });

        // Fire two concurrent downgrade requests
        var downgrade1 = client.PostAsync("/api/billing/downgrade-to-free", null);
        var downgrade2 = client.PostAsync("/api/billing/downgrade-to-free", null);

        await Task.WhenAll(downgrade1, downgrade2);

        // Both should succeed (200)
        (await downgrade1).StatusCode.Should().Be(HttpStatusCode.OK);
        (await downgrade2).StatusCode.Should().Be(HttpStatusCode.OK);

        // Final state: plan is free
        var session = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");
        session!.User.PlanId.Should().Be("free");

        // Only the original payment transaction exists
        var transactions = await client.GetFromJsonAsync<SimulatedPaymentResponse[]>("/api/billing/transactions");
        transactions!.Length.Should().Be(1);
    }

    [Fact]
    public async Task DowngradeToFree_Unauthenticated_Returns401()
    {
        using var client = factory.CreateClient();

        var downgrade = await client.PostAsync("/api/billing/downgrade-to-free", null);
        downgrade.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SimulatePayment_FreePlan_StillRejectsAsPayment()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        var payment = await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "free",
            billingCycle = "monthly"
        });

        payment.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await payment.Content.ReadFromJsonAsync<ProblemResponse>();
        problem!.Title.Should().Be("The free plan does not require payment.");
    }

    [Fact]
    public async Task DowngradeToFree_PreservesPaidBillingHistory()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        // Make a payment
        await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "individual",
            billingCycle = "monthly"
        });

        // Downgrade
        await client.PostAsync("/api/billing/downgrade-to-free", null);

        // Check history preserves the original payment only (no synthetic downgrade record)
        var transactions = await client.GetFromJsonAsync<SimulatedPaymentResponse[]>("/api/billing/transactions");
        transactions!.Length.Should().Be(1);
        transactions.Any(t => t.PlanId == "individual" && t.Amount > 0).Should().BeTrue();
    }

    [Fact]
    public async Task DowngradeToFree_PreservesExistingLinkTokensOverQuota()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        // Upgrade to Family (quota 5)
        await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "family",
            billingCycle = "monthly"
        });

        // Re-login to get fresh JWT with updated plan
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = registration.User.Email, password = "Password1" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResponse = await login.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResponse!.Token);

        // Create 4 link tokens (exceeds Free quota of 1)
        for (int i = 0; i < 4; i++)
        {
            var create = await client.PostAsJsonAsync("/api/tokens", new { role = "family_member" });
            create.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var quotaBefore = await client.GetFromJsonAsync<TokenQuotaResponse>("/api/tokens/quota");
        quotaBefore!.Limit.Should().Be(5);
        quotaBefore.Used.Should().Be(4);

        // Downgrade to Free (quota 1)
        await client.PostAsync("/api/billing/downgrade-to-free", null);

        // Re-login to get fresh JWT with Free plan
        var loginAfter = await client.PostAsJsonAsync("/api/auth/login", new { email = registration.User.Email, password = "Password1" });
        loginAfter.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginAfterResponse = await loginAfter.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginAfterResponse!.Token);

        // Existing tokens remain (no silent deletion)
        var tokens = await client.GetFromJsonAsync<TokenResponse[]>("/api/tokens");
        tokens!.Length.Should().Be(4);

        // New quota reflects Free plan
        var quotaAfter = await client.GetFromJsonAsync<TokenQuotaResponse>("/api/tokens/quota");
        quotaAfter!.Limit.Should().Be(1);
        quotaAfter.Used.Should().Be(4); // Still 4, over quota
        quotaAfter.Remaining.Should().Be(0);

        // Creating new token should fail due to quota
        var createNew = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });
        createNew.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DowngradeToFree_PreservesEpisodesOverFreeQuota()
    {
        using var client = factory.CreateClient();
        var registration = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);

        // Upgrade to Individual (unlimited episodes)
        await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "individual",
            billingCycle = "monthly"
        });

        // Re-login to get fresh JWT with updated plan
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = registration.User.Email, password = "Password1" });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginResponse = await login.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResponse!.Token);

        // Create 7 episodes (exceeds Free quota of 5/week)
        for (int i = 0; i < 7; i++)
        {
            var create = await client.PostAsJsonAsync("/api/episodes", new
            {
                intensity = 50,
                symptoms = new[] { "test" },
                notes = "test"
            });
            create.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Downgrade to Free
        await client.PostAsync("/api/billing/downgrade-to-free", null);

        // Re-login to get fresh JWT with Free plan
        var loginAfter = await client.PostAsJsonAsync("/api/auth/login", new { email = registration.User.Email, password = "Password1" });
        loginAfter.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginAfterResponse = await loginAfter.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginAfterResponse!.Token);

        // Existing episodes remain
        var episodes = await client.GetFromJsonAsync<EpisodeResponse[]>("/api/episodes?range=7");
        episodes!.Length.Should().Be(7);

        // Creating new episode should fail due to weekly quota
        var createNew = await client.PostAsJsonAsync("/api/episodes", new
        {
            intensity = 50,
            symptoms = new[] { "test" },
            notes = "test"
        });
        createNew.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
    private sealed record DowngradeToFreeResponse(string PlanId, string PreviousPlanId, bool Changed, DateTimeOffset? DowngradedAt);
    private sealed record TokenQuotaResponse(int Limit, int Used, int Remaining);
    private sealed record TokenResponse(string Id, string Code, string Role, DateTimeOffset ExpiresAt, string Status);
    private sealed record SimulatedPaymentResponse(string TransactionId, string PlanId, string BillingCycle, decimal Amount, string Currency, string Status, bool Simulated, DateTimeOffset CreatedAt);
    private sealed record EpisodeResponse(string Id, DateTimeOffset Date, int Intensity, IReadOnlyCollection<string> Symptoms, string? Notes);
    private sealed record ProblemResponse(string Title, int Status);
}
