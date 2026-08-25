using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CaregiverPatientEpisodesEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task LinkedCaregiver_ReturnsThePatientsEpisodesNewestFirst()
    {
        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        var (patient, patientId) = await CreateUserAsync("Patient");
        await AddRelationshipAsync(patientId, caregiverId, TokenStatus.Accepted, "family_member");
        await CreateEpisodeAsync(patient, 40, "calm", "first note");
        await CreateEpisodeAsync(patient, 80, "panic", "latest note");

        var response = await caregiver.GetFromJsonAsync<CaregiverEpisodeResponse[]>(
            $"/api/caregiver/patients/{patientId}/episodes");

        response.Should().HaveCount(2);
        response![0].Intensity.Should().Be(80);
        response[0].Symptoms.Should().Contain("panic");
        response[0].Notes.Should().Be("latest note");
        response[0].DetailsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task PrivateMode_RedactsAndRestoresDetailsForSameCaregiverJwt()
    {
        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        var (patient, patientId) = await CreateUserAsync("Patient");
        await AddRelationshipAsync(patientId, caregiverId, TokenStatus.Accepted, "family_member");
        await CreateEpisodeAsync(patient, 70, "panic", "sensitive note");

        var path = $"/api/caregiver/patients/{patientId}/episodes";
        var visible = await caregiver.GetFromJsonAsync<CaregiverEpisodeResponse[]>(path);
        visible![0].Symptoms.Should().Contain("panic");
        visible[0].Notes.Should().Be("sensitive note");
        visible[0].DetailsHidden.Should().BeFalse();

        (await patient.PostAsJsonAsync("/api/billing/simulate-payment", new
        {
            planId = "family",
            billingCycle = "monthly"
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        var settingsResponse = await patient.PatchAsJsonAsync("/api/settings", new
        {
            anxietyThreshold = 70,
            pushNotifications = true,
            privateMode = true
        });
        settingsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await settingsResponse.Content.ReadFromJsonAsync<SettingsResponse>();
        settings!.PrivateMode.Should().BeTrue();

        var hiddenResponse = await caregiver.GetAsync(path);
        var hiddenJson = await hiddenResponse.Content.ReadAsStringAsync();
        var hidden = JsonSerializer.Deserialize<CaregiverEpisodeResponse[]>(
            hiddenJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        hidden[0].Symptoms.Should().BeNull();
        hidden[0].Notes.Should().BeNull();
        hidden[0].DetailsHidden.Should().BeTrue();
        hidden[0].Date.Should().NotBe(default);
        hidden[0].Intensity.Should().Be(70);
        hiddenJson.Should().NotContain("panic");
        hiddenJson.Should().NotContain("sensitive note");

        (await patient.PatchAsJsonAsync("/api/settings", new
        {
            anxietyThreshold = 70,
            pushNotifications = true,
            privateMode = false
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var restored = await caregiver.GetFromJsonAsync<CaregiverEpisodeResponse[]>(path);
        restored![0].Symptoms.Should().Contain("panic");
        restored[0].Notes.Should().Be("sensitive note");
        restored[0].DetailsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task UnauthenticatedAndWrongCaregiver_AreDenied()
    {
        using var anonymous = factory.CreateClient();
        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        var (patient, patientId) = await CreateUserAsync("Patient");
        await AddRelationshipAsync(patientId, caregiverId, TokenStatus.Accepted, "family_member");
        await CreateEpisodeAsync(patient, 50, "symptom", "note");

        (await anonymous.GetAsync($"/api/caregiver/patients/{patientId}/episodes"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var (otherCaregiver, _) = await CreateUserAsync("Other Caregiver");
        var denied = await otherCaregiver.GetAsync($"/api/caregiver/patients/{patientId}/episodes");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(TokenStatus.Pending, "family_member")]
    [InlineData(TokenStatus.Accepted, "self")]
    [InlineData(TokenStatus.Accepted, "patient")]
    public async Task IneligibleRelationships_AreDenied(TokenStatus status, string role)
    {
        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        var (patient, patientId) = await CreateUserAsync("Patient");
        await AddRelationshipAsync(patientId, caregiverId, status, role);
        await CreateEpisodeAsync(patient, 50, "symptom", "note");

        (await caregiver.GetAsync($"/api/caregiver/patients/{patientId}/episodes"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revocation_DeniesSameJwtAndDoesNotAffectSecondCaregiver()
    {
        var (first, firstId) = await CreateUserAsync("First Caregiver");
        var (second, secondId) = await CreateUserAsync("Second Caregiver");
        var (patient, patientId) = await CreateUserAsync("Patient");
        var firstToken = await AddRelationshipAsync(patientId, firstId, TokenStatus.Accepted, "family_member");
        await AddRelationshipAsync(patientId, secondId, TokenStatus.Accepted, "family_member");
        await CreateEpisodeAsync(patient, 60, "symptom", "note");

        (await first.GetAsync($"/api/caregiver/patients/{patientId}/episodes")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.GetAsync($"/api/caregiver/patients/{patientId}/episodes")).StatusCode.Should().Be(HttpStatusCode.OK);
        await WithTokensAsync(tokens => tokens.TryRevokeAsync(firstToken.Id));
        (await first.GetAsync($"/api/caregiver/patients/{patientId}/episodes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await second.GetAsync($"/api/caregiver/patients/{patientId}/episodes")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CaregiverCannotReadAnotherPatientsEpisodes_AndEmptyPatientReturnsEmptyArray()
    {
        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        var (firstPatient, firstId) = await CreateUserAsync("First Patient");
        var (secondPatient, secondId) = await CreateUserAsync("Second Patient");
        await AddRelationshipAsync(firstId, caregiverId, TokenStatus.Accepted, "family_member");
        await CreateEpisodeAsync(firstPatient, 30, "first", "first note");
        await CreateEpisodeAsync(secondPatient, 90, "second", "second note");

        (await caregiver.GetFromJsonAsync<CaregiverEpisodeResponse[]>(
            $"/api/caregiver/patients/{firstId}/episodes"))!.Single().Intensity.Should().Be(30);
        (await caregiver.GetAsync($"/api/caregiver/patients/{secondId}/episodes"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (emptyPatient, emptyId) = await CreateUserAsync("Empty Patient");
        await AddRelationshipAsync(emptyId, caregiverId, TokenStatus.Accepted, "family_member");
        (await caregiver.GetFromJsonAsync<CaregiverEpisodeResponse[]>(
            $"/api/caregiver/patients/{emptyId}/episodes")).Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidRangeMatchesSelfEndpointValidation()
    {
        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        var (patient, patientId) = await CreateUserAsync("Patient");
        await AddRelationshipAsync(patientId, caregiverId, TokenStatus.Accepted, "family_member");
        await CreateEpisodeAsync(patient, 50, "symptom", "note");

        (await caregiver.GetAsync($"/api/caregiver/patients/{patientId}/episodes?range=8"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await caregiver.GetAsync($"/api/episodes?range=8"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidRangesAndDefaultMatchSelfEndpointContract()
    {
        var (caregiver, caregiverId) = await CreateUserAsync("Caregiver");
        var (patient, patientId) = await CreateUserAsync("Patient");
        await AddRelationshipAsync(patientId, caregiverId, TokenStatus.Accepted, "family_member");
        await CreateEpisodeAsync(patient, 50, "symptom", "note");

        foreach (var range in new[] { 7, 30, 90 })
        {
            (await caregiver.GetAsync($"/api/caregiver/patients/{patientId}/episodes?range={range}"))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await caregiver.GetAsync($"/api/caregiver/patients/{patientId}/episodes"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
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

    private static async Task CreateEpisodeAsync(HttpClient patient, int intensity, string symptom, string notes)
    {
        (await patient.PostAsJsonAsync("/api/episodes", new
        {
            intensity,
            symptoms = new[] { symptom },
            notes
        })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<LinkToken> AddRelationshipAsync(
        Guid patientId,
        Guid caregiverId,
        TokenStatus status,
        string role)
    {
        var token = new LinkToken(Guid.NewGuid(), patientId, Code(), role, DateTimeOffset.UtcNow.AddDays(30));
        await WithTokensAsync(tokens => tokens.TryAddAsync(token, 10));
        if (status == TokenStatus.Accepted)
        {
            await WithTokensAsync(tokens => tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, DateTimeOffset.UtcNow));
        }
        else if (status == TokenStatus.Deleted)
        {
            await WithTokensAsync(tokens => tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, DateTimeOffset.UtcNow));
            await WithTokensAsync(tokens => tokens.TryRevokeAsync(token.Id));
        }

        return token;
    }

    private async Task<TResult> WithTokensAsync<TResult>(Func<ILinkTokenRepository, Task<TResult>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>());
    }

    private static string Code() => $"AW-{Guid.NewGuid():N}"[..15].ToUpperInvariant();

    private sealed record AuthResponse(string Token, UserResponse User);
    private sealed record UserResponse(string Id);
    private sealed record CaregiverEpisodeResponse(
        DateTimeOffset Date,
        int Intensity,
        IReadOnlyCollection<string>? Symptoms,
        string? Notes,
        bool DetailsHidden);
    private sealed record SettingsResponse(int AnxietyThreshold, bool PushNotifications, bool PrivateMode);
}
