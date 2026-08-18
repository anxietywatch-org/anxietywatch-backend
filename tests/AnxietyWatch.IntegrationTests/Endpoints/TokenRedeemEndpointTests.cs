using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class TokenRedeemEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task RedeemByCode_ShouldExposeCaregiverRole_AndRejectASecondUse()
    {
        using var owner = await CreateAuthenticatedClient();
        var tokenResponse = await owner.PostAsJsonAsync("/api/tokens", new { role = "family_member" });
        var created = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        using var anonymousClient = factory.CreateClient();
        var firstRedeem = await anonymousClient.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = created!.Code,
            deviceId = "device-1"
        });
        var secondRedeem = await anonymousClient.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = created.Code,
            deviceId = "device-2"
        });

        firstRedeem.StatusCode.Should().Be(HttpStatusCode.OK);
        var redeemed = await firstRedeem.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        redeemed!.Role.Should().Be("family_member");
        redeemed.User.Role.Should().Be("family_member");
        redeemed.User.Email.Should().Contain("caregiver");

        secondRedeem.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await secondRedeem.Content.ReadFromJsonAsync<ProblemResponse>();
        problem!.Title.Should().Be("The code has already been used.");
    }

    [Fact]
    public async Task RedeemByCode_WithUnknownCode_ShouldReturnNotFound()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = "AW-AAAA-BBBB-CCCC",
            deviceId = "device-1"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        problem!.Title.Should().Be("The code is invalid.");
    }

    [Fact]
    public async Task AcceptById_ShouldRejectASecondUse()
    {
        using var client = await CreateAuthenticatedClient();
        var tokenResponse = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });
        var created = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();

        var firstAccept = await client.PostAsJsonAsync($"/api/tokens/{created!.Id}/accept", new
        {
            deviceId = "device-1"
        });
        var secondAccept = await client.PostAsJsonAsync($"/api/tokens/{created.Id}/accept", new
        {
            deviceId = "device-2"
        });

        firstAccept.IsSuccessStatusCode.Should().BeTrue();
        var accepted = await firstAccept.Content.ReadFromJsonAsync<AcceptResponse>();
        accepted!.Status.Should().Be("accepted");

        secondAccept.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await secondAccept.Content.ReadFromJsonAsync<ProblemResponse>();
        problem!.Title.Should().Be("The token has already been used.");
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Token Owner User",
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
    private sealed record TokenResponse(string Id, string Code, string Role);
    private sealed record TokenRedeemResponse(string Token, DateTimeOffset ExpiresAt, string Role, UserResponse User);
    private sealed record UserResponse(
        string Id,
        string FullName,
        string Email,
        string PlanId,
        bool EmailVerified,
        string? AvatarUrl = null,
        string Role = "patient");
    private sealed record AcceptResponse(string Status);
    private sealed record ProblemResponse(string Title, int Status);
}