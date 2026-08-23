using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class TokenRotationEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task RotateOwnedPendingToken_ShouldReturnSameIdNewCodeSameRoleAndFreshExpiration()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var created = await CreateTokenAsync(client, "family_member");

        var response = await client.PostAsync($"/api/tokens/{created.Id}/rotate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        rotated.Id.Should().Be(created.Id);
        rotated.Code.Should().NotBe(created.Code);
        rotated.Role.Should().Be("family_member");
        rotated.Status.Should().Be("pending");
        rotated.ExpiresAt.Should().BeAfter(created.ExpiresAt);
    }

    [Fact]
    public async Task Rotate_ShouldInvalidateOldCodeAndAllowNewCode()
    {
        using var owner = await CreateAuthenticatedClientAsync();
        var created = await CreateTokenAsync(owner, "family_member");
        var rotated = await RotateTokenAsync(owner, created.Id);

        using var anonymous = factory.CreateClient();
        var oldCode = await anonymous.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = created.Code,
            deviceId = "device-old"
        });
        var newCode = await anonymous.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = rotated.Code,
            deviceId = "device-new"
        });
        var rotateAfterAccepted = await owner.PostAsync($"/api/tokens/{created.Id}/rotate", null);

        oldCode.StatusCode.Should().Be(HttpStatusCode.NotFound);
        newCode.StatusCode.Should().Be(HttpStatusCode.OK);
        rotateAfterAccepted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rotateAfterAccepted.Content.ReadFromJsonAsync<ProblemResponse>())!.Title
            .Should().Be("An accepted token cannot be rotated.");
    }

    [Fact]
    public async Task Rotate_FreeQuotaFull_ShouldReuseExistingSlot()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var created = await CreateTokenAsync(client, "self");
        (await client.PostAsJsonAsync("/api/tokens", new { role = "self" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict);

        var rotated = await RotateTokenAsync(client, created.Id);
        var quota = await client.GetFromJsonAsync<TokenQuotaResponse>("/api/tokens/quota");
        var createAfterRotate = await client.PostAsJsonAsync("/api/tokens", new { role = "self" });

        rotated.Id.Should().Be(created.Id);
        quota!.Limit.Should().Be(1);
        quota.Used.Should().Be(1);
        quota.Remaining.Should().Be(0);
        createAfterRotate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Rotate_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/api/tokens/{Guid.NewGuid()}/rotate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rotate_OtherUsersToken_ShouldReturnForbidden()
    {
        using var owner = await CreateAuthenticatedClientAsync();
        var created = await CreateTokenAsync(owner, "self");
        using var other = await CreateAuthenticatedClientAsync();

        var response = await other.PostAsync($"/api/tokens/{created.Id}/rotate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<ProblemResponse>())!.Title
            .Should().Be("The token does not belong to the authenticated user.");
    }

    [Fact]
    public async Task Rotate_DeletedToken_ShouldReturnConflict()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var created = await CreateTokenAsync(client, "self");
        (await client.DeleteAsync($"/api/tokens/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsync($"/api/tokens/{created.Id}/rotate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemResponse>())!.Title
            .Should().Be("The token is no longer available.");
    }

    [Fact]
    public async Task Rotate_ExpiredPendingToken_ShouldRefreshIt()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var auth = (await client.GetFromJsonAsync<AuthResponse>("/api/auth/session"))!;
        var expired = new LinkToken(Guid.NewGuid(), Guid.Parse(auth.User.Id), "AW-OLD1-OLD2-OLD3", "self",
            DateTimeOffset.UtcNow.AddDays(-1));
        using (var scope = factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>();
            (await tokens.TryAddAsync(expired, 1)).Should().BeTrue();
        }

        var response = await client.PostAsync($"/api/tokens/{expired.Id}/rotate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        rotated.Id.Should().Be(expired.Id.ToString());
        rotated.Code.Should().NotBe(expired.Code);
        rotated.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task Rotate_ShouldPreserveExistingDeleteRevokeAndAcceptBehavior()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var deleteCandidate = await RotateTokenAsync(client, (await CreateTokenAsync(client, "self")).Id);
        (await client.DeleteAsync($"/api/tokens/{deleteCandidate.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var acceptedCandidate = await RotateTokenAsync(client, (await CreateTokenAsync(client, "self")).Id);
        (await client.PostAsJsonAsync($"/api/tokens/{acceptedCandidate.Id}/accept", new { deviceId = "device-accept" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/tokens/{acceptedCandidate.Id}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Token Rotation User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private static async Task<TokenResponse> CreateTokenAsync(HttpClient client, string role)
    {
        var response = await client.PostAsJsonAsync("/api/tokens", new { role });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private static async Task<TokenResponse> RotateTokenAsync(HttpClient client, string id)
    {
        var response = await client.PostAsync($"/api/tokens/{id}/rotate", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    private sealed record AuthResponse(string Token, UserResponse User);
    private sealed record UserResponse(string Id);
    private sealed record TokenResponse(string Id, string Code, string Role, DateTimeOffset ExpiresAt, string Status);
    private sealed record TokenQuotaResponse(int Limit, int Used, int Remaining);
    private sealed record ProblemResponse(string Title, int Status);
}
