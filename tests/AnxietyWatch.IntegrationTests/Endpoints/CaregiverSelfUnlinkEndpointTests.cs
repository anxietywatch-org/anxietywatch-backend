using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CaregiverSelfUnlinkEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task ExplicitOnly_UnlinkRemovesAccessFromAllCaregiverEndpoints()
    {
        var (caregiver, caregiverId) = await AddUserAsync("Caregiver", "family_member");
        var (_, patientId) = await AddUserAsync("Patient", "patient");
        await WithServicesAsync(async (links, _) => await links.EnsureLinkAsync(caregiverId, patientId, null, DateTimeOffset.UtcNow));

        (await caregiver.GetAsync($"/api/caregiver/patients/{patientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await caregiver.DeleteAsync($"/api/caregiver/patients/{patientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await caregiver.DeleteAsync($"/api/caregiver/patients/{patientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertNoCaregiverAccessAsync(caregiver, patientId);
        await WithServicesAsync(async (links, _) => (await links.IsLinkedAsync(caregiverId, patientId)).Should().BeFalse());
    }

    [Fact]
    public async Task LegacyOnly_UnlinkRevokesAllMatchingTokensAndPreservesOtherRelationships()
    {
        var (caregiver, caregiverId) = await AddUserAsync("Caregiver", "family_member");
        var (_, patientId) = await AddUserAsync("Patient", "patient");
        var (_, otherPatientId) = await AddUserAsync("Other Patient", "patient");
        var (_, otherCaregiverId) = await AddUserAsync("Other Caregiver", "family_member");
        var first = await AddLegacyAsync(patientId, caregiverId, "first");
        var second = await AddLegacyAsync(patientId, caregiverId, "second");
        await AddLegacyAsync(patientId, otherCaregiverId, "other-caregiver");
        await AddLegacyAsync(otherPatientId, caregiverId, "other-patient");
        var pending = await AddLegacyAsync(Guid.NewGuid(), caregiverId, "pending", accepted: false);
        await WithServicesAsync(async (links, _) =>
        {
            (await links.IsLinkedAsync(caregiverId, patientId)).Should().BeFalse();
            (await links.IsLinkedAsync(caregiverId, otherPatientId)).Should().BeFalse();
        });

        (await caregiver.GetAsync($"/api/caregiver/patients/{patientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await caregiver.DeleteAsync($"/api/caregiver/patients/{patientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertNoCaregiverAccessAsync(caregiver, patientId);
        (await caregiver.GetAsync($"/api/caregiver/patients/{otherPatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        await WithServicesAsync(async (_, tokens) =>
        {
            (await tokens.GetByIdAsync(first)).Should().Match<LinkToken>(x => x.Status == TokenStatus.Deleted);
            (await tokens.GetByIdAsync(second)).Should().Match<LinkToken>(x => x.Status == TokenStatus.Deleted);
            (await tokens.GetByIdAsync(pending)).Should().Match<LinkToken>(x => x.Status == TokenStatus.Pending);
            (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, otherCaregiverId)).Should().BeTrue();
            (await tokens.HasAcceptedCaregiverRelationshipAsync(otherPatientId, caregiverId)).Should().BeTrue();
        });
    }

    [Fact]
    public async Task Hybrid_UnlinkRemovesExplicitAndLegacyRepresentations()
    {
        var (caregiver, caregiverId) = await AddUserAsync("Caregiver", "family_member");
        var (_, patientId) = await AddUserAsync("Patient", "patient");
        var tokenId = await AddLegacyAsync(patientId, caregiverId, "hybrid");
        await WithServicesAsync(async (links, _) => await links.EnsureLinkAsync(caregiverId, patientId, null, DateTimeOffset.UtcNow));

        (await caregiver.DeleteAsync($"/api/caregiver/patients/{patientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertNoCaregiverAccessAsync(caregiver, patientId);
        await WithServicesAsync(async (links, tokens) =>
        {
            (await links.IsLinkedAsync(caregiverId, patientId)).Should().BeFalse();
            (await tokens.GetByIdAsync(tokenId))!.Status.Should().Be(TokenStatus.Deleted);
        });
    }

    [Fact]
    public async Task UnlinkPreservesOtherCaregiverPatientUserJwtAndInvitation()
    {
        var (caregiver, caregiverId) = await AddUserAsync("Caregiver", "family_member");
        var (_, otherCaregiverId) = await AddUserAsync("Other Caregiver", "family_member");
        var (_, patientOneId) = await AddUserAsync("Patient One", "patient");
        var (_, patientTwoId) = await AddUserAsync("Patient Two", "patient");
        var invitation = new CaregiverInvitation(Guid.NewGuid(), Guid.NewGuid(), patientOneId, Code(), DateTimeOffset.UtcNow.AddDays(1));
        invitation.Accept(caregiverId, DateTimeOffset.UtcNow);
        await WithServicesAsync(async (_, _, invitations) => await invitations.AddAsync(invitation));
        await WithServicesAsync(async (links, _) =>
        {
            await links.EnsureLinkAsync(caregiverId, patientOneId, invitation.Id, DateTimeOffset.UtcNow);
            await links.EnsureLinkAsync(caregiverId, patientTwoId, null, DateTimeOffset.UtcNow);
            await links.EnsureLinkAsync(otherCaregiverId, patientOneId, null, DateTimeOffset.UtcNow);
        });
        var jwtSub = new JwtSecurityTokenHandler().ReadJwtToken(caregiver.DefaultRequestHeaders.Authorization!.Parameter!).Subject;

        (await caregiver.DeleteAsync($"/api/caregiver/patients/{patientOneId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await caregiver.GetAsync("/api/auth/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        new JwtSecurityTokenHandler().ReadJwtToken(caregiver.DefaultRequestHeaders.Authorization!.Parameter!).Subject.Should().Be(jwtSub);
        (await caregiver.GetAsync($"/api/caregiver/patients/{patientTwoId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await caregiver.GetAsync($"/api/caregiver/patients/{patientOneId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var other = ClientFor(await GetUserAsync(otherCaregiverId));
        (await other.GetAsync($"/api/caregiver/patients/{patientOneId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await WithServicesAsync(async (_, _, invitations) => (await invitations.GetByCodeAsync(invitation.Code))!.Status.Should().Be(CaregiverInvitationStatus.Accepted));
    }

    [Fact]
    public async Task NonCaregiverCannotUnlinkAndHasNoSideEffects()
    {
        var (patientClient, patientId) = await AddUserAsync("Patient", "patient");
        var (caregiver, caregiverId) = await AddUserAsync("Caregiver", "family_member");
        var tokenId = await AddLegacyAsync(patientId, caregiverId, "protected");
        await WithServicesAsync(async (links, _) => await links.EnsureLinkAsync(caregiverId, patientId, null, DateTimeOffset.UtcNow));

        (await patientClient.DeleteAsync($"/api/caregiver/patients/{patientId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await WithServicesAsync(async (links, tokens) =>
        {
            (await links.IsLinkedAsync(caregiverId, patientId)).Should().BeTrue();
            (await tokens.GetByIdAsync(tokenId))!.Status.Should().Be(TokenStatus.Accepted);
        });
    }

    [Fact]
    public async Task ConcurrentUnlinkRequestsAreSafeAndFinishWithNoAccess()
    {
        var (caregiver, caregiverId) = await AddUserAsync("Caregiver", "family_member");
        var (_, patientId) = await AddUserAsync("Patient", "patient");
        await AddLegacyAsync(patientId, caregiverId, "concurrent");
        await WithServicesAsync(async (links, _) => await links.EnsureLinkAsync(caregiverId, patientId, null, DateTimeOffset.UtcNow));

        var responses = await Task.WhenAll(
            caregiver.DeleteAsync($"/api/caregiver/patients/{patientId}"),
            caregiver.DeleteAsync($"/api/caregiver/patients/{patientId}"));
        responses.Should().OnlyContain(x => x.StatusCode == HttpStatusCode.NoContent);
        await AssertNoCaregiverAccessAsync(caregiver, patientId);
    }

    private async Task<(HttpClient Client, Guid Id)> AddUserAsync(string name, string role)
    {
        var id = Guid.NewGuid();
        var user = new User(id, name, $"{id:N}@example.test", "hash", role == "family_member" ? "free" : "free", role);
        await WithServicesAsync(async (_, _, _, users) => await users.AddAsync(user));
        return (ClientFor(user), id);
    }

    private async Task<User> GetUserAsync(Guid id)
    {
        User? user = null;
        await WithServicesAsync(async (_, _, _, users) => user = await users.GetByIdAsync(id));
        return user!;
    }

    private async Task<Guid> AddLegacyAsync(Guid patientId, Guid caregiverId, string label, bool accepted = true)
    {
        var token = new LinkToken(Guid.NewGuid(), patientId, $"AW-{label}-{Guid.NewGuid():N}"[..15].ToUpperInvariant(), "family_member", DateTimeOffset.UtcNow.AddDays(1));
        await WithServicesAsync(async (_, tokens) =>
        {
            (await tokens.TryAddAsync(token, 20)).Should().BeTrue();
            if (accepted) (await tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, DateTimeOffset.UtcNow)).Should().BeTrue();
        });
        return token.Id;
    }

    private HttpClient ClientFor(User user)
    {
        using var scope = factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().Create(user.Id, user.Email, user.PlanId, user.SecurityVersion);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt.AccessToken);
        return client;
    }

    private async Task AssertNoCaregiverAccessAsync(HttpClient client, Guid patientId)
    {
        (await client.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients"))!.Should().NotContain(x => x.PatientId == patientId.ToString());
        foreach (var path in new[] { "", "/episodes", "/events", "/telemetry/latest", "/heart-rate/latest" })
            (await client.GetAsync($"/api/caregiver/patients/{patientId}{path}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task WithServicesAsync(Func<ICaregiverPatientLinkRepository, ILinkTokenRepository, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ICaregiverPatientLinkRepository>(), scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>());
    }

    private async Task WithServicesAsync(Func<ICaregiverPatientLinkRepository, ILinkTokenRepository, ICaregiverInvitationRepository, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ICaregiverPatientLinkRepository>(), scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>(), scope.ServiceProvider.GetRequiredService<ICaregiverInvitationRepository>());
    }

    private async Task WithServicesAsync(Func<ICaregiverPatientLinkRepository, ILinkTokenRepository, ICaregiverInvitationRepository, IUserRepository, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ICaregiverPatientLinkRepository>(), scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>(), scope.ServiceProvider.GetRequiredService<ICaregiverInvitationRepository>(), scope.ServiceProvider.GetRequiredService<IUserRepository>());
    }

    private static string Code() => $"invite-{Guid.NewGuid():N}";
    private sealed record LinkedPatientResponse(string PatientId);
}
