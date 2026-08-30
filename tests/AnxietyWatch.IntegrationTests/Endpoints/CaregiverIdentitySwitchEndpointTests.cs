using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class CaregiverIdentitySwitchEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task AuthenticatedCaregiverAcceptingSecondCode_ReusesSameIdentityAndKeepsBothPatients()
    {
        var (caregiver, caregiverId, first) = await AcceptAnonymouslyAsync("Patient P1");
        var second = await CreatePatientInvitationAsync("Patient P2");
        var firstSub = ReadSubject(caregiver.DefaultRequestHeaders.Authorization!.Parameter!);

        var response = await caregiver.PostAsJsonAsync("/api/tokens/accept-by-code", new { code = second.Code, deviceId = "device-2" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await response.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        var secondSub = ReadSubject(session!.Token);
        secondSub.Should().Be(firstSub);
        session.User.Id.Should().Be(caregiverId.ToString());

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        (await users.GetByIdAsync(second.TokenId)).Should().BeNull();
        var tokens = scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>();
        (await tokens.GetByIdAsync(first.TokenId))!.AcceptedBy.Should().Be(caregiverId);
        (await tokens.GetByIdAsync(second.TokenId))!.AcceptedBy.Should().Be(caregiverId);
        caregiver.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var patients = await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients");
        patients!.Select(x => x.PatientId).Should().BeEquivalentTo([first.PatientId, second.PatientId]);
        (await caregiver.GetAsync($"/api/caregiver/patients/{first.PatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await caregiver.GetAsync($"/api/caregiver/patients/{second.PatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthenticatedNonCaregiverCannotUseCaregiverInvitationToCreateAccount()
    {
        var patientClient = await factory.CreateAuthenticatedClientAsync();
        var invitation = await CreatePatientInvitationAsync("Invited Patient");
        var response = await patientClient.PostAsJsonAsync("/api/tokens/accept-by-code", new { code = invitation.Code, deviceId = "patient-device" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var scope = factory.Services.CreateScope();
        var token = await scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>().GetByIdAsync(invitation.TokenId);
        token!.Status.Should().Be(TokenStatus.Pending);
        token.AcceptedBy.Should().BeNull();
        token.AcceptedAt.Should().BeNull();
        (await scope.ServiceProvider.GetRequiredService<IUserRepository>().GetByIdAsync(invitation.TokenId)).Should().BeNull();
    }

    [Fact]
    public async Task ActivatedCaregiverAcceptingSecondCode_PreservesIdentityAndBothRelationships()
    {
        var (caregiver, caregiverId, first) = await AcceptAnonymouslyAsync("Patient P1");
        ReadSubject(caregiver.DefaultRequestHeaders.Authorization!.Parameter!).Should().Be(caregiverId.ToString());

        var activationResponse = await caregiver.PostAsJsonAsync("/api/auth/caregiver/activate", new
        {
            email = $"caregiver-{caregiverId:N}@example.test",
            password = "CaregiverPassword1"
        });
        activationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var activated = await activationResponse.Content.ReadFromJsonAsync<AuthResponse>();
        activated!.User.Id.Should().Be(caregiverId.ToString());
        ReadSubject(activated.Token).Should().Be(caregiverId.ToString());

        var second = await CreatePatientInvitationAsync("Patient P2");
        caregiver.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", activated.Token);
        var response = await caregiver.PostAsJsonAsync("/api/tokens/accept-by-code", new { code = second.Code, deviceId = "activated-device" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var redeemed = await response.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        ReadSubject(redeemed!.Token).Should().Be(caregiverId.ToString());
        ReadSubject(redeemed.Token).Should().NotBe(second.TokenId.ToString());
        redeemed.User.Id.Should().Be(caregiverId.ToString());

        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>();
        (await tokens.GetByIdAsync(first.TokenId))!.AcceptedBy.Should().Be(caregiverId);
        (await tokens.GetByIdAsync(second.TokenId))!.AcceptedBy.Should().Be(caregiverId);
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        (await users.GetByIdAsync(second.TokenId)).Should().BeNull();

        caregiver.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", redeemed.Token);
        var patients = await caregiver.GetFromJsonAsync<LinkedPatientResponse[]>("/api/caregiver/patients");
        patients!.Select(x => x.PatientId).Should().BeEquivalentTo([first.PatientId, second.PatientId]);
        (await caregiver.GetAsync($"/api/caregiver/patients/{first.PatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await caregiver.GetAsync($"/api/caregiver/patients/{second.PatientId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnonymousFamilyMemberOnboardingStillUsesTokenIdAndSecondUseIsRejected()
    {
        var invitation = await CreatePatientInvitationAsync("Onboarding Patient");
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/tokens/accept-by-code", new { code = invitation.Code, deviceId = "onboarding-device" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await response.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        session!.User.Id.Should().Be(invitation.TokenId.ToString());
        ReadSubject(session.Token).Should().Be(invitation.TokenId.ToString());
        (await anonymous.PostAsJsonAsync("/api/tokens/accept-by-code", new { code = invitation.Code, deviceId = "retry" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<(HttpClient Client, Guid CaregiverId, Invitation Invitation)> AcceptAnonymouslyAsync(string patientName)
    {
        var invitation = await CreatePatientInvitationAsync(patientName);
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/tokens/accept-by-code", new { code = invitation.Code, deviceId = "onboarding-device" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await response.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!.Token);
        return (client, invitation.TokenId, invitation);
    }

    private async Task<Invitation> CreatePatientInvitationAsync(string name)
    {
        var patientId = Guid.NewGuid(); var tokenId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUserRepository>().AddAsync(new User(patientId, name, $"{patientId:N}@example.test", "hash", "free", "patient"));
        var token = new LinkToken(tokenId, patientId, $"AW-{tokenId:N}"[..15].ToUpperInvariant(), "family_member", DateTimeOffset.UtcNow.AddDays(1));
        (await scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>().TryAddAsync(token, 10)).Should().BeTrue();
        return new(tokenId, patientId, token.Code);
    }

    private static string ReadSubject(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token).Subject!;

    private sealed record Invitation(Guid TokenId, Guid PatientId, string Code);
    private sealed record AuthResponse(string Token, DateTimeOffset ExpiresAt, SessionUser User);
    private sealed record TokenRedeemResponse(string Token, DateTimeOffset ExpiresAt, string Role, SessionUser User);
    private sealed record SessionUser(string Id);
    private sealed record LinkedPatientResponse(Guid PatientId);
}
