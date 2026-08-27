using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Domain.Caregivers;
using Microsoft.Extensions.DependencyInjection;
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
        (await ownerClient.PostAsJsonAsync("/api/episodes", new
        {
            intensity = 65,
            symptoms = new[] { "owner-symptom" },
            notes = "owner episode note"
        })).StatusCode.Should().Be(HttpStatusCode.Created);
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

        using (var scope = factory.Services.CreateScope())
        {
            var audit = await scope.ServiceProvider
                .GetRequiredService<ICaregiverRelationshipAuditRepository>()
                .GetAsync(caregiverId: Guid.Parse(caregiverId));
            audit.Should().ContainSingle(item =>
                item.Action == CaregiverRelationshipAuditAction.AcceptedInitial);
        }

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
        var patientDetail = await relogin.GetFromJsonAsync<PatientDetailResponse>(
            $"/api/caregiver/patients/{linkedPatients[0].PatientId}");
        patientDetail!.FullName.Should().Be("owner");
        patientDetail.PatientId.Should().Be(linkedPatients[0].PatientId);
        var episodes = await relogin.GetFromJsonAsync<PatientEpisodeResponse[]>(
            $"/api/caregiver/patients/{linkedPatients[0].PatientId}/episodes");
        episodes.Should().ContainSingle();
        episodes![0].Intensity.Should().Be(65);
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

    [Fact]
    public async Task ConcurrentActivation_ShouldHaveExactlyOneWinner()
    {
        var caregiver = await RedeemCaregiverAsync();
        using var requestA = new HttpRequestMessage(HttpMethod.Post, "/api/auth/caregiver/activate")
        {
            Content = JsonContent.Create(new { email = "caregiver-a@example.test", password = "CaregiverPassword1" })
        };
        using var requestB = new HttpRequestMessage(HttpMethod.Post, "/api/auth/caregiver/activate")
        {
            Content = JsonContent.Create(new { email = "caregiver-b@example.test", password = "CaregiverPassword2" })
        };

        var responses = await Task.WhenAll(
            caregiver.Client.SendAsync(requestA),
            caregiver.Client.SendAsync(requestB));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(response => response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Unauthorized).Should().Be(1);
        var winner = responses.Single(response => response.StatusCode == HttpStatusCode.OK);
        var auth = await winner.Content.ReadFromJsonAsync<AuthResponse>();
        auth!.User.Email.Should().BeOneOf("caregiver-a@example.test", "caregiver-b@example.test");

        var winnerPassword = auth.User.Email.Contains("-a@", StringComparison.Ordinal)
            ? "CaregiverPassword1"
            : "CaregiverPassword2";
        var losingEmail = auth.User.Email.Contains("-a@", StringComparison.Ordinal)
            ? "caregiver-b@example.test"
            : "caregiver-a@example.test";
        var losingPassword = auth.User.Email.Contains("-a@", StringComparison.Ordinal)
            ? "CaregiverPassword2"
            : "CaregiverPassword1";

        using var login = factory.CreateClient();
        var loginResponse = await login.PostAsJsonAsync("/api/auth/login", new
        {
            email = auth.User.Email,
            password = winnerPassword
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var losingLoginResponse = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            email = losingEmail,
            password = losingPassword
        });
        losingLoginResponse.IsSuccessStatusCode.Should().BeFalse();

        var loggedIn = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        login.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loggedIn!.Token);
        (await login.GetAsync("/api/auth/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await login.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients"))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task ActivatedCaregiver_ShouldUseExistingVerificationResendFlow()
    {
        var caregiver = await RedeemCaregiverAsync();
        var activation = await caregiver.Client.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = "caregiver-verification@example.test",
            password = "CaregiverPassword1"
        });
        var auth = await activation.Content.ReadFromJsonAsync<AuthResponse>();
        caregiver.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        var resend = await caregiver.Client.PostAsync("/api/auth/verify-email/resend", null);

        resend.StatusCode.Should().Be(HttpStatusCode.OK);
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
    private sealed record PatientDetailResponse(string PatientId, string FullName, string? AvatarUrl);
    private sealed record PatientEpisodeResponse(
        DateTimeOffset Date,
        int Intensity,
        IReadOnlyCollection<string>? Symptoms,
        string? Notes,
        bool DetailsHidden);
}
