using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CaregiverActivationEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task RedeemActivateReloginAndListPatients_ShouldPreserveIdentityAndRelationship()
    {
        var owner = await RegisterAsync("owner");
        using var ownerClient = owner.Client;
        var tokenResponse = await ownerClient.PostAsJsonAsync("/api/tokens", new { role = "family_member" });
        var created = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();

        using var caregiver = factory.CreateClient();
        var redeemedResponse = await caregiver.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = created!.Code,
            deviceId = "caregiver-device"
        });
        var redeemed = await redeemedResponse.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        var caregiverId = redeemed!.User.Id;
        caregiver.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", redeemed.Token);

        var activation = await caregiver.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = "  caregiver@example.test ",
            password = "CaregiverPassword1"
        });
        var activated = await activation.Content.ReadFromJsonAsync<AuthResponse>();

        activation.StatusCode.Should().Be(HttpStatusCode.OK);
        activated!.User.Id.Should().Be(caregiverId);
        activated.User.Email.Should().Be("caregiver@example.test");
        activated.User.EmailVerified.Should().BeFalse();

        using var relogin = factory.CreateClient();
        var login = await relogin.PostAsJsonAsync("/api/auth/login", new
        {
            email = "CAREGIVER@example.test",
            password = "CaregiverPassword1"
        });
        var loggedIn = await login.Content.ReadFromJsonAsync<AuthResponse>();
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        loggedIn!.User.Id.Should().Be(caregiverId);

        relogin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loggedIn.Token);
        (await relogin.GetAsync("/api/auth/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        var linkedPatients = await relogin.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients");
        linkedPatients.Should().ContainSingle();
        linkedPatients![0].FullName.Should().Be("owner");
    }

    [Fact]
    public async Task Activation_ShouldRejectDuplicateEmailAndRepeatedActivation()
    {
        var existing = await RegisterAsync("existing");
        using var existingClient = existing.Client;
        var caregiver = await RedeemCaregiverAsync();

        var duplicate = await caregiver.Client.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = existing.Email,
            password = "CaregiverPassword1"
        });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var activation = await caregiver.Client.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = "caregiver-unique@example.test",
            password = "CaregiverPassword1"
        });
        activation.StatusCode.Should().Be(HttpStatusCode.OK);
        caregiver.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await activation.Content.ReadFromJsonAsync<AuthResponse>())!.Token);

        var replay = await caregiver.Client.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = "caregiver-other@example.test",
            password = "CaregiverPassword2"
        });
        replay.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Activation_ShouldRejectUnauthenticatedAndInvalidPassword()
    {
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = "caregiver@example.test",
            password = "CaregiverPassword1"
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var caregiver = await RedeemCaregiverAsync();
        var invalid = await caregiver.Client.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = "caregiver@example.test",
            password = "short"
        });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Activation_ShouldInvalidateInitialJwt()
    {
        var caregiver = await RedeemCaregiverAsync();
        var initialToken = caregiver.Client.DefaultRequestHeaders.Authorization!.Parameter;
        var activation = await caregiver.Client.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = "caregiver-security@example.test",
            password = "CaregiverPassword1"
        });
        activation.StatusCode.Should().Be(HttpStatusCode.OK);

        caregiver.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", initialToken);
        (await caregiver.Client.GetAsync("/api/auth/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(HttpClient Client, string Email)> RegisterAsync(string suffix)
    {
        var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}-{suffix}@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = suffix,
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return (client, email);
    }

    private async Task<(HttpClient Client, string Id)> RedeemCaregiverAsync()
    {
        var owner = await RegisterAsync("token-owner");
        using var ownerClient = owner.Client;
        var tokenResponse = await ownerClient.PostAsJsonAsync("/api/tokens", new { role = "family_member" });
        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
        var client = factory.CreateClient();
        var redeemedResponse = await client.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = token!.Code,
            deviceId = Guid.NewGuid().ToString("N")
        });
        var redeemed = await redeemedResponse.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", redeemed!.Token);
        return (client, redeemed.User.Id);
    }

    private sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, UserResponse User);
    private sealed record TokenResponse(string Code);
    private sealed record TokenRedeemResponse(string Token, DateTimeOffset ExpiresAt, string Role, UserResponse User);
    private sealed record UserResponse(string Id, string FullName, string Email, string PlanId, bool EmailVerified, string? AvatarUrl = null, string Role = "patient");
    private sealed record LinkedPatientResponse(string PatientId, string FullName, string? AvatarUrl, string Role, DateTimeOffset LinkedAt);
}
