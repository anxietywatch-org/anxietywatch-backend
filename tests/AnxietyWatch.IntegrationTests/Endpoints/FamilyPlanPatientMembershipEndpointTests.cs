using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Domain.FamilyPlans;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AnxietyWatch.Infrastructure.FamilyPlans;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class FamilyPlanPatientMembershipEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task FamilyOwnerPatientOnboarding_CreatesMembershipAndListsOnlyAcceptedPatient()
    {
        var owner = await RegisterOwnerAsync();
        var ownerId = Guid.Parse(owner.User.Id);
        await SetPlanAsync(ownerId, "family");

        var create = await owner.Client.PostAsJsonAsync("/api/tokens", new { role = "patient" });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = await create.Content.ReadFromJsonAsync<TokenResponse>();

        using var onboarding = factory.CreateClient();
        var accept = await onboarding.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = invitation!.Code,
            deviceId = "patient-device"
        });
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await accept.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        session!.Token.Should().NotBeNullOrWhiteSpace();
        var patientId = Guid.Parse(session.User.Id);
        var unrelatedPatientId = Guid.NewGuid();
        using (var unrelatedScope = factory.Services.CreateScope())
        {
            var users = unrelatedScope.ServiceProvider.GetRequiredService<IUserRepository>();
            await users.AddAsync(new User(unrelatedPatientId, "Unrelated Patient", $"{unrelatedPatientId}@example.test", "hash", "free"));
        }

        using var scope = factory.Services.CreateScope();
        var memberships = scope.ServiceProvider.GetRequiredService<IFamilyPlanPatientMembershipRepository>();
        var authorizer = scope.ServiceProvider.GetRequiredService<AnxietyWatch.Application.Features.FamilyPlans.IFamilyPlanPatientAuthorizer>();
        (await memberships.ListPatientsAsync(ownerId)).Should().ContainSingle().Which.Should().Match<FamilyPlanPatientMembership>(x => x.OwnerUserId == ownerId && x.PatientUserId == patientId);
        (await authorizer.CanManagePatientAsync(ownerId, patientId)).Should().BeTrue();

        var familyPatients = await owner.Client.GetFromJsonAsync<FamilyPatientResponse[]>("/api/family/patients");
        familyPatients.Should().ContainSingle().Which.PatientId.Should().Be(patientId.ToString());
        familyPatients[0].Name.Should().Be(session.User.FullName);
        familyPatients.Should().NotContain(x => x.PatientId == unrelatedPatientId.ToString());
        owner.Client.Dispose();
    }

    [Fact]
    public async Task StartupReconciliationService_ExecutesReconciler()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var tokens = scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>();
        var memberships = scope.ServiceProvider.GetRequiredService<IFamilyPlanPatientMembershipRepository>();
        var ownerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await users.AddAsync(new User(ownerId, "Startup Owner", "startup-owner@example.test", "hash", "family"));
        await users.AddAsync(new User(patientId, "Startup Patient", "startup-patient@example.test", "hash", "free"));
        var token = LinkToken.Restore(Guid.NewGuid(), ownerId, "AW-STARTUP", "patient", now.AddHours(1), TokenStatus.Accepted, patientId, now);
        await tokens.TryAddAsync(token, 10);
        var service = new FamilyPlanPatientMembershipReconciliationService(
            scope.ServiceProvider.GetRequiredService<AnxietyWatch.Application.Features.FamilyPlans.FamilyPlanPatientMembershipReconciler>(),
            scope.ServiceProvider.GetRequiredService<ILogger<FamilyPlanPatientMembershipReconciliationService>>());

        await service.StartAsync(CancellationToken.None);
        service.ExecuteTask.Should().NotBeNull();
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        (await memberships.CanManagePatientAsync(ownerId, patientId)).Should().BeTrue();
        scope.Dispose();
    }

    private async Task<OwnerRegistration> RegisterOwnerAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Family Plan Owner",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var registration = (await response.Content.ReadFromJsonAsync<RegistrationPayload>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registration.Token);
        return new OwnerRegistration(registration.Token, registration.User, client);
    }

    private async Task SetPlanAsync(Guid ownerId, string planId)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        (await users.UpdatePlanAsync(ownerId, planId)).Should().BeTrue();
    }

    private sealed record RegistrationPayload(string Token, OwnerUser User);
    private sealed record OwnerRegistration(string Token, OwnerUser User, HttpClient Client);
    private sealed record OwnerUser(string Id, string PlanId);
    private sealed record TokenResponse(string Id, string Code, string Role);
    private sealed record TokenRedeemResponse(string Token, DateTimeOffset ExpiresAt, string Role, PatientUser User);
    private sealed record PatientUser(string Id, string FullName);
    private sealed record FamilyPatientResponse(string PatientId, string Name);
}
