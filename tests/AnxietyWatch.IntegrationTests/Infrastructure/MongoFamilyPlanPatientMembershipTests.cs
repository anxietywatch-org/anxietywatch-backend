using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Features.FamilyPlans;
using AnxietyWatch.Domain.FamilyPlans;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoFamilyPlanPatientMembershipTests : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context;
    private readonly MongoFamilyPlanPatientMembershipRepository memberships;
    private readonly MongoUserRepository users;
    private readonly MongoLinkTokenRepository tokens;

    public MongoFamilyPlanPatientMembershipTests(MongoDbContainerFixture fixture)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
            ["Mongo:DatabaseName"] = $"anxietywatch_family_tests_{Guid.NewGuid():N}"
        }).Build();
        context = new MongoContext(configuration);
        memberships = new MongoFamilyPlanPatientMembershipRepository(context);
        users = new MongoUserRepository(context);
        tokens = new MongoLinkTokenRepository(context);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task EnsureMembership_InsertsAndIsIdempotent()
    {
        var owner = Guid.NewGuid();
        var patient = Guid.NewGuid();
        var first = await memberships.EnsureMembershipAsync(owner, patient, null, DateTimeOffset.UtcNow);
        var second = await memberships.EnsureMembershipAsync(owner, patient, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));

        second.Id.Should().Be(first.Id);
        (await memberships.ListPatientsAsync(owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureMembership_ConcurrentSamePairLeavesOneRecord()
    {
        var owner = Guid.NewGuid();
        var patient = Guid.NewGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => memberships.EnsureMembershipAsync(owner, patient, null, DateTimeOffset.UtcNow)));

        results.Select(x => x.Id).Distinct().Should().ContainSingle();
        (await memberships.ListPatientsAsync(owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task CanManage_RequiresTheExactStoredPair()
    {
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var patient = Guid.NewGuid();
        await memberships.EnsureMembershipAsync(owner, patient, null, DateTimeOffset.UtcNow);

        (await memberships.CanManagePatientAsync(owner, patient)).Should().BeTrue();
        (await memberships.CanManagePatientAsync(owner, Guid.NewGuid())).Should().BeFalse();
        (await memberships.CanManagePatientAsync(otherOwner, patient)).Should().BeFalse();
    }

    [Fact]
    public async Task ListPatients_FiltersInactiveRecords()
    {
        var owner = Guid.NewGuid();
        await memberships.EnsureMembershipAsync(owner, Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        await context.Database.GetCollection<BsonDocument>("family_plan_patient_memberships").InsertOneAsync(new BsonDocument
        {
            ["_id"] = Guid.NewGuid().ToString(),
            ["ownerUserId"] = owner.ToString(),
            ["patientUserId"] = Guid.NewGuid().ToString(),
            ["createdAt"] = DateTime.UtcNow,
            ["status"] = "Revoked"
        });

        (await memberships.ListPatientsAsync(owner)).Should().ContainSingle();
    }

    [Fact]
    public async Task Reconciliation_CreatesMembershipAndRemainsIdempotent()
    {
        var ownerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await users.AddAsync(new User(ownerId, "Owner", "mongo-owner@example.test", "hash", "family"));
        await users.AddAsync(new User(patientId, "Patient", "mongo-patient@example.test", "hash", "free"));
        var token = LinkToken.Restore(Guid.NewGuid(), ownerId, "AW-MONGO", "patient", now.AddHours(1), TokenStatus.Accepted, patientId, now);
        await tokens.TryAddAsync(token, 10);
        var reconciler = new FamilyPlanPatientMembershipReconciler(tokens, users, memberships, new FixedClock(now), LoggerFactory.Create(_ => { }).CreateLogger<FamilyPlanPatientMembershipReconciler>());

        (await reconciler.ReconcileAcceptedPatientTokensAsync()).Should().Be(1);
        (await reconciler.ReconcileAcceptedPatientTokensAsync()).Should().Be(1);
        (await memberships.ListPatientsAsync(ownerId)).Should().ContainSingle().Which.PatientUserId.Should().Be(patientId);
    }

    private sealed class FixedClock(DateTimeOffset value) : ISystemClock
    {
        public DateTimeOffset UtcNow => value;
    }
}
