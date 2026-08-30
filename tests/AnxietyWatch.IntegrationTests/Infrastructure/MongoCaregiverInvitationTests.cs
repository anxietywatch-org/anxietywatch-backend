using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoCaregiverInvitationTests(MongoDbContainerFixture fixture) : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context = CreateContext(fixture);
    private MongoCaregiverInvitationRepository Invitations => new(context);
    private MongoCaregiverPatientLinkRepository Links => new(context);

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task InvitationInsertAndUniqueCodeArePersisted()
    {
        var first = Invitation("unique"); await Invitations.AddAsync(first);
        (await Invitations.GetByCodeAsync("unique"))!.TargetPatientId.Should().Be(first.TargetPatientId);
        var duplicate = Invitation("unique");
        await FluentActions.Awaiting(() => Invitations.AddAsync(duplicate)).Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task PendingToAcceptedPersistsAcceptedIdentityAndTime()
    {
        var invitation = Invitation("transition"); await Invitations.AddAsync(invitation); var acceptedAt = DateTimeOffset.UtcNow;
        var accepted = await Invitations.TryAcceptAsync(invitation.Id, invitation.TargetPatientId, acceptedAt);
        accepted!.Status.Should().Be(CaregiverInvitationStatus.Accepted);
        accepted.AcceptedByCaregiverId.Should().Be(invitation.TargetPatientId);
        accepted.AcceptedAt.Should().BeCloseTo(acceptedAt, TimeSpan.FromSeconds(1));
        (await Invitations.TryAcceptAsync(invitation.Id, Guid.NewGuid(), acceptedAt)).Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentAcceptHasExactlyOneWinner()
    {
        var invitation = Invitation("race"); await Invitations.AddAsync(invitation);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Invitations.TryAcceptAsync(invitation.Id, Guid.NewGuid(), DateTimeOffset.UtcNow)));
        results.Count(x => x is not null).Should().Be(1);
        (await context.Database.GetCollection<BsonDocument>("caregiver_invitations").CountDocumentsAsync(new BsonDocument("_id", invitation.Id.ToString()))).Should().Be(1);
    }

    [Fact]
    public async Task ExpiredInvitationIsRejectedByApplicationHandler()
    {
        var patient = Guid.NewGuid(); var expired = Invitation("expired", patient, DateTimeOffset.UtcNow.AddMinutes(-1)); await Invitations.AddAsync(expired);
        var users = new MongoUserRepository(context); await users.AddAsync(new User(patient, "Patient", $"{patient:N}@example.test", "hash", "free"));
        var current = new FixedCurrentUser(Guid.NewGuid());
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var act = () => new AcceptCaregiverInvitationHandler(current, users, Invitations, Links, clock).Handle(new("expired"), default);
        await act.Should().ThrowAsync<ConflictException>();
        (await Invitations.GetByCodeAsync("expired"))!.Status.Should().Be(CaregiverInvitationStatus.Pending);
    }

    [Fact]
    public async Task LinkIsUniqueIdempotentConcurrentAndIndependentOfInvitation()
    {
        var caregiver = Guid.NewGuid(); var patient = Guid.NewGuid(); var invitation = Invitation("link", patient); await Invitations.AddAsync(invitation);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Links.EnsureLinkAsync(caregiver, patient, invitation.Id, DateTimeOffset.UtcNow)));
        results.Select(x => x.Id).Distinct().Should().ContainSingle();
        (await Links.IsLinkedAsync(caregiver, patient)).Should().BeTrue();
        (await Links.IsLinkedAsync(Guid.NewGuid(), patient)).Should().BeFalse();
        (await Links.ListByCaregiverAsync(caregiver)).Should().ContainSingle().Which.SourceInvitationId.Should().Be(invitation.Id);
        await Invitations.TryDeleteAsync(invitation.Id, invitation.IssuedByUserId);
        (await Links.IsLinkedAsync(caregiver, patient)).Should().BeTrue();
        (await Links.ListByCaregiverAsync(caregiver)).Should().ContainSingle();
    }

    private CaregiverInvitation Invitation(string code, Guid? patient = null, DateTimeOffset? expires = null) => new(Guid.NewGuid(), Guid.NewGuid(), patient ?? Guid.NewGuid(), code, expires ?? DateTimeOffset.UtcNow.AddDays(1));
    private static MongoContext CreateContext(MongoDbContainerFixture fixture) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(), ["Mongo:DatabaseName"] = $"anxietywatch_caregiver_tests_{Guid.NewGuid():N}" }).Build());
    private sealed class FixedCurrentUser(Guid id) : ICurrentUser { public bool IsAuthenticated => true; public Guid UserId => id; public string? Email => null; public string? PlanId => "free"; public string? JwtId => null; public DateTimeOffset? TokenExpiresAt => null; }
    private sealed class FixedClock(DateTimeOffset value) : ISystemClock { public DateTimeOffset UtcNow => value; }
}
