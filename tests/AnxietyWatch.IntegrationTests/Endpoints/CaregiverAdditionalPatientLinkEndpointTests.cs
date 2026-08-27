using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CaregiverAdditionalPatientLinkEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task ExistingCaregiverLinksSecondPatientWithoutReplacingSessionOrFirstRelationship()
    {
        var (caregiver, caregiverId) = await CreateCaregiverAsync();
        var first = await CreateInvitationAsync();
        (await LinkAsync(caregiver, first.Code)).StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await CreateInvitationAsync();

        var response = await LinkAsync(caregiver, second.Code);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var linked = await response.Content.ReadFromJsonAsync<LinkResponse>();
        linked!.PatientId.Should().Be(second.PatientId);
        linked.Role.Should().Be("family_member");
        var session = await caregiver.GetAsync("/api/auth/session");
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        (await session.Content.ReadFromJsonAsync<SessionResponse>())!.User.Id.Should().Be(caregiverId.ToString());
        var patients = await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients");
        patients!.Select(patient => patient.PatientId).Should().BeEquivalentTo([first.PatientId, second.PatientId]);
        (await caregiver.GetAsync($"/api/caregiver/patients/{first.PatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await caregiver.GetAsync($"/api/caregiver/patients/{second.PatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await WithAuditAsync(repository => repository.GetAsync(caregiverId: caregiverId));
        audit.Should().HaveCount(2);
        audit.Select(item => item.Action).Should().Equal(
            CaregiverRelationshipAuditAction.AcceptedAdditional,
            CaregiverRelationshipAuditAction.AcceptedAdditional);
    }

    [Fact]
    public async Task SameCodeRaceAllowsExactlyOneCaregiverToLink()
    {
        var (first, _) = await CreateCaregiverAsync();
        var (second, _) = await CreateCaregiverAsync();
        var invitation = await CreateInvitationAsync();

        var responses = await Task.WhenAll(LinkAsync(first, invitation.Code), LinkAsync(second, invitation.Code));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    [Fact]
    public async Task SameCaregiverCanConcurrentlyLinkDifferentPatients()
    {
        var (caregiver, _) = await CreateCaregiverAsync();
        var first = await CreateInvitationAsync();
        var second = await CreateInvitationAsync();

        var responses = await Task.WhenAll(LinkAsync(caregiver, first.Code), LinkAsync(caregiver, second.Code));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        (await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients")).Should().HaveCount(2);
    }

    [Fact]
    public async Task RevokingOnePatientLeavesOtherPatientAndSessionIntact()
    {
        var (caregiver, _) = await CreateCaregiverAsync();
        var first = await CreateInvitationAsync();
        var second = await CreateInvitationAsync();
        await LinkAsync(caregiver, first.Code);
        await LinkAsync(caregiver, second.Code);

        await WithTokensAsync(tokens => tokens.TryRevokeAsync(first.TokenId));

        var patients = await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients");
        patients.Should().ContainSingle(patient => patient.PatientId == second.PatientId);
        (await caregiver.GetAsync($"/api/caregiver/patients/{first.PatientId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await caregiver.GetAsync($"/api/caregiver/patients/{second.PatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await caregiver.GetAsync("/api/auth/session")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UnknownExpiredRevokedAndAlreadyUsedCodesAreRejectedSafely()
    {
        var (caregiver, _) = await CreateCaregiverAsync();
        var expired = await CreateInvitationAsync(DateTimeOffset.UtcNow.AddMinutes(-1));
        var revoked = await CreateInvitationAsync();
        await WithTokensAsync(tokens => tokens.TryDeleteAsync(revoked.TokenId, revoked.Code));
        var used = await CreateInvitationAsync();
        var (other, _) = await CreateCaregiverAsync();
        await LinkAsync(other, used.Code);

        (await LinkAsync(caregiver, "AW-NOT-FOUND")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LinkAsync(caregiver, expired.Code)).StatusCode.Should().Be(HttpStatusCode.Gone);
        (await LinkAsync(caregiver, revoked.Code)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LinkAsync(caregiver, used.Code)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("self")]
    [InlineData("patient")]
    public async Task NonFamilyMemberInvitationCannotCreateCaregiverRelationship(string role)
    {
        var (caregiver, _) = await CreateCaregiverAsync();
        var invitation = await CreateInvitationAsync(role: role);

        (await LinkAsync(caregiver, invitation.Code)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients")).Should().BeEmpty();
    }

    [Fact]
    public async Task PatientAccountAndAnonymousCallerCannotLinkInvitation()
    {
        var invitation = await CreateInvitationAsync();
        using var anonymous = factory.CreateClient();
        var patient = await CreatePatientClientAsync();

        (await LinkAsync(anonymous, invitation.Code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LinkAsync(patient, invitation.Code)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateCaregiverAsync()
    {
        var id = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var user = new User(id, "Caregiver", $"{id:N}@example.test", "unused", "free", "family_member");
        await services.GetRequiredService<IUserRepository>().AddAsync(user);
        var jwt = services.GetRequiredService<IJwtTokenService>().Create(id, user.Email, user.PlanId, user.SecurityVersion);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.AccessToken);
        return (client, id);
    }

    private async Task<HttpClient> CreatePatientClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Patient",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await response.Content.ReadFromJsonAsync<SessionResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private async Task<Invitation> CreateInvitationAsync(
        DateTimeOffset? expiresAt = null,
        string role = "family_member")
    {
        var patient = await CreatePatientClientAsync();
        var session = await patient.GetFromJsonAsync<SessionResponse>("/api/auth/session");
        patient.Dispose();
        var patientId = Guid.Parse(session!.User.Id);
        var token = new LinkToken(Guid.NewGuid(), patientId, Code(), role, expiresAt ?? DateTimeOffset.UtcNow.AddDays(1));
        (await WithTokensAsync(tokens => tokens.TryAddAsync(token, 10))).Should().BeTrue();
        return new Invitation(token.Id, patientId, token.Code);
    }

    private static Task<HttpResponseMessage> LinkAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync("/api/caregiver/patients/link", new { code });

    private async Task<TResult> WithTokensAsync<TResult>(Func<ILinkTokenRepository, Task<TResult>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>());
    }

    private async Task<TResult> WithAuditAsync<TResult>(Func<ICaregiverRelationshipAuditRepository, Task<TResult>> action)
    {
        using var scope = factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICaregiverRelationshipAuditRepository>());
    }

    private static string Code() => $"AW-{Guid.NewGuid():N}"[..15].ToUpperInvariant();

    private sealed record Invitation(Guid TokenId, Guid PatientId, string Code);
    private sealed record LinkResponse(Guid PatientId, string Role);
    private sealed record SessionResponse(string Token, SessionUser User);
    private sealed record SessionUser(string Id);
    private sealed record LinkedPatientResponse(Guid PatientId);
}
