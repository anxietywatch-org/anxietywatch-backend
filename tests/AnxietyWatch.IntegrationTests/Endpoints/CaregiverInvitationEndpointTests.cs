using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.FamilyPlans;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CaregiverInvitationEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task OwnerInvitesPatient_CaregiverAccepts_AndAllCaregiverEndpointsUseNewLink()
    {
        var setup = await SetupAsync();
        var invite = await CreateInviteAsync(setup.Owner, setup.PatientId);
        var accepted = await AcceptAsync(setup.Caregiver, invite.Code);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LinkAsync(setup.CaregiverId, setup.PatientId)).Should().BeTrue();
        (await LinkAsync(setup.OwnerId, setup.PatientId)).Should().BeFalse();

        var patients = await setup.Caregiver.GetFromJsonAsync<LinkedPatient[]>("/api/caregiver/patients");
        patients.Should().ContainSingle(x => x.PatientId == setup.PatientId && x.FullName == "Patient P");
        (await setup.Caregiver.GetAsync($"/api/caregiver/patients/{setup.PatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await setup.Caregiver.GetAsync($"/api/caregiver/patients/{setup.PatientId}/episodes")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await setup.Caregiver.GetAsync($"/api/caregiver/patients/{setup.PatientId}/telemetry/latest")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await setup.Caregiver.GetAsync($"/api/caregiver/patients/{setup.PatientId}/events")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InvitationBodyIdsAreIgnored_AndWrongOwnerIsForbidden()
    {
        var setup = await SetupAsync();
        var invite = await CreateInviteAsync(setup.Owner, setup.PatientId);
        var body = new { code = invite.Code, patientId = Guid.NewGuid(), caregiverId = Guid.NewGuid(), issuerId = Guid.NewGuid() };
        (await setup.Caregiver.PostAsJsonAsync("/api/caregiver/invitations/accept", body)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LinkAsync(setup.CaregiverId, setup.PatientId)).Should().BeTrue();
        var unrelatedPatient = await AddUserAsync("Patient Q", "free", "patient");
        (await setup.Owner.PostAsync($"/api/caregiver/patients/{unrelatedPatient.Id}/invitations", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var otherOwner = await AddUserAsync("Owner B", "family", "patient");
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFamilyPlanPatientMembershipRepository>().EnsureMembershipAsync(otherOwner.Id, unrelatedPatient.Id, null, DateTimeOffset.UtcNow);
        var otherClient = ClientFor(otherOwner);
        (await otherClient.PostAsync($"/api/caregiver/patients/{setup.PatientId}/invitations", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task InvalidExpiredTakenAndSelfInvitationsAreRejected()
    {
        var setup = await SetupAsync();
        (await AcceptAsync(setup.Caregiver, "missing-code")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var expired = new CaregiverInvitation(Guid.NewGuid(), setup.OwnerId, setup.PatientId, $"expired-{Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddMinutes(-1));
        var self = new CaregiverInvitation(Guid.NewGuid(), setup.OwnerId, setup.CaregiverId, $"self-{Guid.NewGuid():N}", DateTimeOffset.UtcNow.AddDays(1));
        await AddInvitationAsync(expired); await AddInvitationAsync(self);
        (await AcceptAsync(setup.Caregiver, expired.Code)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AcceptAsync(setup.Caregiver, self.Code)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LinkAsync(setup.CaregiverId, setup.PatientId)).Should().BeFalse();
    }

    [Fact]
    public async Task TakenInvitationRetryAndSecondCodeRemainDeterministic()
    {
        var setup = await SetupAsync();
        var first = await CreateInviteAsync(setup.Owner, setup.PatientId);
        (await AcceptAsync(setup.Caregiver, first.Code)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AcceptAsync(setup.Caregiver, first.Code)).StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await CreateInviteAsync(setup.Owner, setup.PatientId);
        (await AcceptAsync(setup.Caregiver, second.Code)).StatusCode.Should().Be(HttpStatusCode.OK);
        var other = await AddUserAsync("Caregiver D", "free", "family_member");
        (await AcceptAsync(ClientFor(other), first.Code)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var links = scope.ServiceProvider.GetRequiredService<ICaregiverPatientLinkRepository>();
        (await links.ListByCaregiverAsync(setup.CaregiverId)).Should().ContainSingle();
        (await links.ListByCaregiverAsync(other.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task PendingInvitationCanBeRevokedByIssuerOnly_AndAcceptedLinkSurvives()
    {
        var setup = await SetupAsync();
        var pending = await CreateInviteAsync(setup.Owner, setup.PatientId);
        var invitationId = await InvitationIdAsync(pending.Code);
        (await setup.Owner.DeleteAsync($"/api/caregiver/invitations/{invitationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AcceptAsync(setup.Caregiver, pending.Code)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var accepted = await CreateInviteAsync(setup.Owner, setup.PatientId);
        (await AcceptAsync(setup.Caregiver, accepted.Code)).StatusCode.Should().Be(HttpStatusCode.OK);
        var acceptedId = await InvitationIdAsync(accepted.Code);
        (await setup.Owner.DeleteAsync($"/api/caregiver/invitations/{acceptedId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LinkAsync(setup.CaregiverId, setup.PatientId)).Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkedCaregiverCannotReadPatient()
    {
        var setup = await SetupAsync();
        var other = await AddUserAsync("Caregiver D", "free", "family_member");
        var client = ClientFor(other);
        (await client.GetAsync($"/api/caregiver/patients/{setup.PatientId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/caregiver/patients/{setup.PatientId}/episodes")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/caregiver/patients/{setup.PatientId}/telemetry/latest")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/caregiver/patients/{setup.PatientId}/events")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Setup> SetupAsync()
    {
        var owner = await AddUserAsync("Owner A", "family", "patient");
        var patient = await AddUserAsync("Patient P", "free", "patient");
        var caregiver = await AddUserAsync("Caregiver C", "free", "family_member");
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IFamilyPlanPatientMembershipRepository>().EnsureMembershipAsync(owner.Id, patient.Id, null, DateTimeOffset.UtcNow);
        return new(owner.Id, patient.Id, caregiver.Id, ClientFor(owner), ClientFor(caregiver));
    }

    private async Task<User> AddUserAsync(string name, string plan, string role)
    {
        var id = Guid.NewGuid(); var user = new User(id, name, $"{id:N}@example.test", "hash", plan, role);
        using var scope = factory.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(user); return user;
    }

    private HttpClient ClientFor(User user)
    {
        using var scope = factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().Create(user.Id, user.Email, user.PlanId, user.SecurityVersion);
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.AccessToken); return client;
    }

    private async Task<(string Code, DateTimeOffset ExpiresAt)> CreateInviteAsync(HttpClient owner, Guid patientId)
    {
        var response = await owner.PostAsync($"/api/caregiver/patients/{patientId}/invitations", null); response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<InvitationResponse>(); return (result!.Code, result.ExpiresAt);
    }
    private Task<HttpResponseMessage> AcceptAsync(HttpClient client, string code) => client.PostAsJsonAsync("/api/caregiver/invitations/accept", new { code });
    private async Task AddInvitationAsync(CaregiverInvitation invitation) { using var scope = factory.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<ICaregiverInvitationRepository>().AddAsync(invitation); }
    private async Task<Guid> InvitationIdAsync(string code) { using var scope = factory.Services.CreateScope(); return (await scope.ServiceProvider.GetRequiredService<ICaregiverInvitationRepository>().GetByCodeAsync(code))!.Id; }
    private async Task<bool> LinkAsync(Guid caregiverId, Guid patientId) { using var scope = factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ICaregiverPatientLinkRepository>().IsLinkedAsync(caregiverId, patientId); }

    private sealed record Setup(Guid OwnerId, Guid PatientId, Guid CaregiverId, HttpClient Owner, HttpClient Caregiver);
    private sealed record InvitationResponse(string Code, DateTimeOffset ExpiresAt);
    private sealed record LinkedPatient(Guid PatientId, string FullName, string? AvatarUrl, string Role, DateTimeOffset LinkedAt);
}
