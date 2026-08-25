using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CaregiverPatientsEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task OneAcceptedPatient_ReturnsPatientSelectorFields()
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        var patientId = await CreateLinkedPatientAsync(caregiverId, "Patient One", DateTimeOffset.UtcNow);

        var response = await caregiver.GetAsync("/api/caregiver/patients");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var patients = await response.Content.ReadFromJsonAsync<LinkedPatientResponse[]>();
        patients.Should().ContainSingle();
        patients![0].PatientId.Should().Be(patientId.ToString());
        patients[0].FullName.Should().Be("Patient One");
        patients[0].AvatarUrl.Should().BeNull();
        patients[0].Role.Should().Be("family_member");
    }

    [Fact]
    public async Task MultiplePatients_ReturnsBothInLinkedAtDescendingOrder()
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        var older = DateTimeOffset.UtcNow.AddMinutes(-10);
        var newer = DateTimeOffset.UtcNow.AddMinutes(-1);
        var olderPatient = await CreateLinkedPatientAsync(caregiverId, "Older Patient", older);
        var newerPatient = await CreateLinkedPatientAsync(caregiverId, "Newer Patient", newer);

        var patients = await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients");

        patients.Should().HaveCount(2);
        patients!.Select(patient => patient.PatientId).Should().Equal(newerPatient.ToString(), olderPatient.ToString());
    }

    [Fact]
    public async Task UnlinkedCaregiver_ReturnsEmptyList()
    {
        var (caregiver, _) = await CreateAuthenticatedUserAsync("Caregiver User");

        var response = await caregiver.GetAsync("/api/caregiver/patients");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<LinkedPatientResponse[]>()).Should().BeEmpty();
    }

    [Theory]
    [InlineData(TokenStatus.Pending, "family_member")]
    [InlineData(TokenStatus.Deleted, "family_member")]
    [InlineData(TokenStatus.Pending, "self")]
    [InlineData(TokenStatus.Accepted, "self")]
    [InlineData(TokenStatus.Accepted, "patient")]
    public async Task InactiveOrNonFamilyMemberRelationships_DoNotAppear(TokenStatus status, string role)
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        await CreateLinkedPatientAsync(caregiverId, "Hidden Patient", DateTimeOffset.UtcNow, status, role);

        (await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients")).Should().BeEmpty();
    }

    [Fact]
    public async Task ExpiredPendingRelationship_DoesNotAppear()
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        await CreateLinkedPatientAsync(
            caregiverId,
            "Expired Pending Patient",
            DateTimeOffset.UtcNow,
            TokenStatus.Pending,
            "family_member",
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        (await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients")).Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptedRelationshipPastOriginalExpiresAt_StillAppears()
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        var now = DateTimeOffset.UtcNow;
        var patientId = await CreateLinkedPatientAsync(
            caregiverId,
            "Accepted Expired Credential Patient",
            now,
            expiresAt: now.AddMinutes(5));

        var afterOriginalExpiry = now.AddMinutes(6);
        var patients = await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients");

        patients.Should().ContainSingle(patient => patient.PatientId == patientId.ToString());
        patients!.Single().LinkedAt.Should().BeBefore(afterOriginalExpiry);
    }

    [Fact]
    public async Task OtherCaregiverRelationship_DoesNotAppear()
    {
        var (caregiver, _) = await CreateAuthenticatedUserAsync("Caregiver Two");
        var (_, otherCaregiverId) = await CreateAuthenticatedUserAsync("Caregiver One");
        await CreateLinkedPatientAsync(otherCaregiverId, "Other Caregiver Patient", DateTimeOffset.UtcNow);

        (await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients")).Should().BeEmpty();
    }

    [Fact]
    public async Task Revocation_RemovesPatientImmediatelyForSameJwt()
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        var token = await CreateLinkedTokenAsync(caregiverId, "Revoked Patient", DateTimeOffset.UtcNow);
        (await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients"))
            .Should().ContainSingle();

        await WithTokensAsync(tokens => tokens.TryRevokeAsync(token.Id));

        (await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients")).Should().BeEmpty();
    }

    [Fact]
    public async Task Response_DoesNotExposeSensitiveFields()
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        await CreateLinkedPatientAsync(caregiverId, "Safe Patient", DateTimeOffset.UtcNow);

        var json = await caregiver.GetStringAsync("/api/caregiver/patients");

        using var document = JsonDocument.Parse(json);
        var patient = document.RootElement.EnumerateArray().Single();
        patient.TryGetProperty("patientId", out _).Should().BeTrue();
        patient.TryGetProperty("fullName", out _).Should().BeTrue();
        patient.TryGetProperty("avatarUrl", out _).Should().BeTrue();
        patient.TryGetProperty("role", out _).Should().BeTrue();
        patient.TryGetProperty("linkedAt", out _).Should().BeTrue();
        foreach (var field in new[] { "email", "password", "passwordHash", "securityVersion", "planId", "token", "code", "quotaSlot", "acceptedBy", "privateMode" })
        {
            patient.TryGetProperty(field, out _).Should().BeFalse(field);
        }
    }

    [Fact]
    public async Task Unauthenticated_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/caregiver/patients")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DuplicateActiveRelationships_ReturnPatientOnceWithEarliestLinkedAt()
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        var patientId = await CreatePatientAsync("Duplicate Patient");
        var earlier = DateTimeOffset.UtcNow.AddMinutes(-10);
        var later = DateTimeOffset.UtcNow.AddMinutes(-1);
        await AddRelationshipAsync(patientId, caregiverId, TokenStatus.Accepted, "family_member", earlier);
        await AddRelationshipAsync(patientId, caregiverId, TokenStatus.Accepted, "family_member", later);

        var patients = await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients");

        patients.Should().ContainSingle();
        patients![0].PatientId.Should().Be(patientId.ToString());
        patients[0].LinkedAt.Should().Be(earlier);
    }

    [Fact]
    public async Task AcceptedRelationshipWithMissingPatientUser_IsSkipped()
    {
        var (caregiver, caregiverId) = await CreateAuthenticatedUserAsync("Caregiver User");
        await AddRelationshipAsync(Guid.NewGuid(), caregiverId, TokenStatus.Accepted, "family_member", DateTimeOffset.UtcNow);

        (await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients")).Should().BeEmpty();
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedUserAsync(string fullName)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName,
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var registration = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration!.Token);
        return (client, Guid.Parse(registration.User.Id));
    }

    private async Task<Guid> CreatePatientAsync(string fullName)
    {
        var (client, userId) = await CreateAuthenticatedUserAsync(fullName);
        client.Dispose();
        return userId;
    }

    private async Task<Guid> CreateLinkedPatientAsync(
        Guid caregiverId,
        string fullName,
        DateTimeOffset linkedAt,
        TokenStatus status = TokenStatus.Accepted,
        string role = "family_member",
        DateTimeOffset? expiresAt = null)
    {
        var token = await CreateLinkedTokenAsync(caregiverId, fullName, linkedAt, status, role, expiresAt);
        return token.UserId;
    }

    private async Task<LinkToken> CreateLinkedTokenAsync(
        Guid caregiverId,
        string fullName,
        DateTimeOffset linkedAt,
        TokenStatus status = TokenStatus.Accepted,
        string role = "family_member",
        DateTimeOffset? expiresAt = null)
    {
        var patientId = await CreatePatientAsync(fullName);
        return await AddRelationshipAsync(patientId, caregiverId, status, role, linkedAt, expiresAt);
    }

    private async Task<LinkToken> AddRelationshipAsync(
        Guid patientId,
        Guid caregiverId,
        TokenStatus status,
        string role,
        DateTimeOffset linkedAt,
        DateTimeOffset? expiresAt = null)
    {
        var token = new LinkToken(Guid.NewGuid(), patientId, Code(), role, expiresAt ?? linkedAt.AddDays(30));
        await WithTokensAsync(tokens => tokens.TryAddAsync(token, 10));
        if (status == TokenStatus.Accepted)
        {
            await WithTokensAsync(tokens => tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, linkedAt));
        }
        else if (status == TokenStatus.Deleted)
        {
            await WithTokensAsync(tokens => tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, linkedAt));
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
    private sealed record LinkedPatientResponse(
        string PatientId,
        string FullName,
        string? AvatarUrl,
        string Role,
        DateTimeOffset LinkedAt);
}
