using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Domain.Notifications;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Notifications;
using AnxietyWatch.Infrastructure.Persistence;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoCaregiverRelationshipNotificationTests : IClassFixture<MongoDbContainerFixture>
{
    private readonly MongoDbContainerFixture fixture;

    public MongoCaregiverRelationshipNotificationTests(MongoDbContainerFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task ListByPatientAsync_ReturnsOnlyCaregiversForRequestedPatient()
    {
        var (context, links) = CreateRepositories();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        await links.EnsureLinkAsync(c1, p1, null, DateTimeOffset.UtcNow.AddMinutes(-3));
        await links.EnsureLinkAsync(c2, p1, null, DateTimeOffset.UtcNow.AddMinutes(-2));
        await links.EnsureLinkAsync(c1, p2, null, DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await links.ListByPatientAsync(p1);

        result.Select(link => link.CaregiverId).Should().BeEquivalentTo([c1, c2]);
        result.Should().OnlyContain(link => link.PatientId == p1);
        await context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public async Task ExplicitLink_PersistsNotificationOutboxJob()
    {
        var (context, links) = CreateRepositories();
        var devices = new MongoDeviceTokenRepository(context);
        var outbox = new MongoNotificationOutboxRepository(context);
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        await links.EnsureLinkAsync(caregiverId, patientId, null, DateTimeOffset.UtcNow);
        var device = new DeviceToken(Guid.NewGuid(), caregiverId, "android", $"registration-{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        await devices.UpsertAsync(device);
        IUserRepository users = new StubUserRepository(new User(patientId, "Patient", "patient@example.test", "hash", "family"));
        ILinkTokenRepository tokens = new InMemoryLinkTokenRepository();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var sut = new CaregiverNotificationOutbox(new CaregiverRelationshipResolver(tokens, links), devices, users, outbox, clock, NullLogger<CaregiverNotificationOutbox>.Instance);

        var eventId = Guid.NewGuid();
        await sut.EnsureNotificationJobsAsync(patientId, eventId, CaregiverNotificationType.Sos);

        var jobs = await outbox.GetAllAsync();
        jobs.Should().ContainSingle();
        jobs[0].PatientId.Should().Be(patientId);
        jobs[0].CaregiverId.Should().Be(caregiverId);
        jobs[0].DeviceRegistrationId.Should().Be(device.Id);
        jobs[0].NotificationType.Should().Be(CaregiverNotificationType.Sos);
        await context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public async Task StartupCreatesPatientLookupIndex()
    {
        var (context, _) = CreateRepositories();
        using var cursor = await context.Database.GetCollection<BsonDocument>("caregiver_patient_links").Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();

        indexes.Should().Contain(index => index["key"].AsBsonDocument.Contains("patientId"));
        await context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);
    }

    private (MongoContext Context, MongoCaregiverPatientLinkRepository Links) CreateRepositories()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
                ["Mongo:DatabaseName"] = $"anxietywatch_fcm_tests_{Guid.NewGuid():N}"
            })
            .Build();
        var context = new MongoContext(configuration);
        return (context, new MongoCaregiverPatientLinkRepository(context));
    }

    private sealed class StubUserRepository(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<User?>(id == user.Id ? user : null);
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdatePlanAsync(Guid id, string planId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> UpdatePasswordAsync(Guid id, string passwordHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> TryActivateCaregiverAsync(Guid id, long expectedVersion, string expectedEmail, string email, string passwordHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> RegisterFailedLoginAsync(Guid id, DateTimeOffset now, string expectedPasswordHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> RegisterSuccessfulLoginAsync(Guid id, DateTimeOffset now, long expectedVersion, string expectedPasswordHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<EmailVerificationTokenState?> StoreEmailVerificationTokenAsync(Guid id, DateTimeOffset sentAt, string tokenHash, DateTimeOffset expiresAt, long expectedVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ConfirmEmailAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RollbackEmailVerificationTokenAsync(Guid id, string tokenHash, DateTimeOffset sentAt, EmailVerificationTokenState previousState, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
