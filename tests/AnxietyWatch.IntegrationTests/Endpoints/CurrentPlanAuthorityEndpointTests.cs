using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CurrentPlanAuthorityEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task TokenCreation_WithPaidJwtAndPersistedFreePlan_ShouldEnforceFreeQuota()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        await SimulatePaymentAsync(client, "family");
        var paidSession = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", paidSession!.Token);

        for (var index = 0; index < 5; index++)
        {
            (await client.PostAsJsonAsync("/api/tokens", new { role = "self" })).StatusCode
                .Should().Be(HttpStatusCode.Created);
        }

        await UpdatePersistedPlanAsync(Guid.Parse(auth.User.Id), "free");

        var stalePaidJwtResponse = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });

        stalePaidJwtResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task EpisodeCreation_WithPaidJwtAndPersistedFreePlan_ShouldEnforceFreeWeeklyQuota()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        await SimulatePaymentAsync(client, "family");
        var paidSession = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", paidSession!.Token);

        for (var index = 0; index < 5; index++)
        {
            (await CreateEpisodeAsync(client, index)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await UpdatePersistedPlanAsync(Guid.Parse(auth.User.Id), "free");

        var stalePaidJwtResponse = await CreateEpisodeAsync(client, 50);

        stalePaidJwtResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PrivateMode_WithPaidJwtAndPersistedFreePlan_ShouldBeDenied()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        await SimulatePaymentAsync(client, "individual");
        var paidSession = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", paidSession!.Token);
        await UpdatePersistedPlanAsync(Guid.Parse(auth.User.Id), "free");

        var response = await client.PatchAsJsonAsync("/api/settings", new
        {
            anxietyThreshold = 55,
            pushNotifications = true,
            privateMode = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Upgrade_WithFreeJwtAndPersistedPaidPlan_ShouldAllowPaidPrivateMode()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        await SimulatePaymentAsync(client, "individual");

        var response = await client.PatchAsJsonAsync("/api/settings", new
        {
            anxietyThreshold = 55,
            pushNotifications = true,
            privateMode = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Upgrade_WithFreeJwtAndPersistedFamilyPlan_ShouldAllowFamilyTokenQuota()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        await SimulatePaymentAsync(client, "family");

        var firstResponse = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });
        var secondResponse = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Session_ShouldReturnPersistedPlan()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        await UpdatePersistedPlanAsync(Guid.Parse(auth.User.Id), "professional");

        var session = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");

        session!.User.PlanId.Should().Be("professional");
    }

    [Fact]
    public async Task ValidJwtAuthentication_ShouldContinueUsingUserIdentity()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        await UpdatePersistedPlanAsync(Guid.Parse(auth.User.Id), "family");

        var response = await client.GetAsync("/api/tokens");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StaleJwtPlanClaim_DoesNotGrantPaidPrivileges()
    {
        using var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        await SimulatePaymentAsync(client, "family");
        var paidSession = await client.GetFromJsonAsync<AuthResponse>("/api/auth/session");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", paidSession!.Token);
        await UpdatePersistedPlanAsync(Guid.Parse(auth.User.Id), "free");

        var response = await client.PatchAsJsonAsync("/api/settings", new
        {
            anxietyThreshold = 55,
            pushNotifications = true,
            privateMode = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task UpdatePersistedPlanAsync(Guid userId, string planId)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        (await users.UpdatePlanAsync(userId, planId)).Should().BeTrue();
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Current Plan Authority User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private static async Task SimulatePaymentAsync(HttpClient client, string planId)
    {
        var response = await client.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId,
            billingCycle = "monthly"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static Task<HttpResponseMessage> CreateEpisodeAsync(HttpClient client, int intensity) =>
        client.PostAsJsonAsync("/api/episodes", new
        {
            intensity,
            symptoms = Array.Empty<string>(),
            notes = "test"
        });

    private sealed record AuthResponse(string Token, UserResponse User);
    private sealed record UserResponse(string Id, string PlanId);
}
