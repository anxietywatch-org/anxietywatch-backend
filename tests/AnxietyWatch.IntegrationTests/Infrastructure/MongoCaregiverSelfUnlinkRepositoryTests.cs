using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoCaregiverSelfUnlinkRepositoryTests(MongoDbContainerFixture fixture) : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context = CreateContext(fixture);
    private MongoCaregiverPatientLinkRepository Links => new(context);
    private MongoLinkTokenRepository Tokens => new(context);

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task RemoveLink_DeletesOnlyExactCaregiverPatientPair_AndIsIdempotent()
    {
        var caregiver = Guid.NewGuid();
        var otherCaregiver = Guid.NewGuid();
        var patient = Guid.NewGuid();
        var otherPatient = Guid.NewGuid();
        await Links.EnsureLinkAsync(caregiver, patient, null, DateTimeOffset.UtcNow);
        await Links.EnsureLinkAsync(caregiver, otherPatient, null, DateTimeOffset.UtcNow);
        await Links.EnsureLinkAsync(otherCaregiver, patient, null, DateTimeOffset.UtcNow);

        (await Links.RemoveLinkAsync(caregiver, patient)).Should().BeTrue();
        (await Links.RemoveLinkAsync(caregiver, patient)).Should().BeFalse();
        (await Links.IsLinkedAsync(caregiver, patient)).Should().BeFalse();
        (await Links.IsLinkedAsync(caregiver, otherPatient)).Should().BeTrue();
        (await Links.IsLinkedAsync(otherCaregiver, patient)).Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAcceptedRelationships_RevokesAllExactLegacyPairsOnly_AndIsIdempotent()
    {
        var caregiver = Guid.NewGuid();
        var otherCaregiver = Guid.NewGuid();
        var patient = Guid.NewGuid();
        var otherPatient = Guid.NewGuid();
        var first = await AddTokenAsync(patient, caregiver, "family_member", accepted: true);
        var second = await AddTokenAsync(patient, caregiver, "family_member", accepted: true);
        var otherCaregiverToken = await AddTokenAsync(patient, otherCaregiver, "family_member", accepted: true);
        var otherPatientToken = await AddTokenAsync(otherPatient, caregiver, "family_member", accepted: true);
        var pending = await AddTokenAsync(patient, caregiver, "family_member", accepted: false);
        var otherRole = await AddTokenAsync(patient, caregiver, "self", accepted: true);

        (await Tokens.RevokeAcceptedCaregiverRelationshipsAsync(patient, caregiver)).Should().Be(2);
        (await Tokens.RevokeAcceptedCaregiverRelationshipsAsync(patient, caregiver)).Should().Be(0);
        (await Tokens.GetByIdAsync(first))!.Status.Should().Be(TokenStatus.Deleted);
        (await Tokens.GetByIdAsync(second))!.Status.Should().Be(TokenStatus.Deleted);
        (await Tokens.GetByIdAsync(otherCaregiverToken))!.Status.Should().Be(TokenStatus.Accepted);
        (await Tokens.GetByIdAsync(otherPatientToken))!.Status.Should().Be(TokenStatus.Accepted);
        (await Tokens.GetByIdAsync(pending))!.Status.Should().Be(TokenStatus.Pending);
        (await Tokens.GetByIdAsync(otherRole))!.Status.Should().Be(TokenStatus.Accepted);
    }

    private async Task<Guid> AddTokenAsync(Guid patientId, Guid caregiverId, string role, bool accepted)
    {
        var token = new LinkToken(Guid.NewGuid(), patientId, $"AW-{Guid.NewGuid():N}"[..15].ToUpperInvariant(), role, DateTimeOffset.UtcNow.AddDays(1));
        (await Tokens.TryAddAsync(token, 20)).Should().BeTrue();
        if (accepted) (await Tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, DateTimeOffset.UtcNow)).Should().BeTrue();
        return token.Id;
    }

    private static MongoContext CreateContext(MongoDbContainerFixture fixture) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
            ["Mongo:DatabaseName"] = $"anxietywatch_unlink_tests_{Guid.NewGuid():N}"
        }).Build());
}
