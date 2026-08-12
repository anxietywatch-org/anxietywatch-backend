using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using AnxietyWatch.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoCorePersistenceTests : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context;
    private readonly MongoUserRepository users;
    private readonly MongoLinkTokenRepository tokens;
    private readonly MongoRevokedTokenStore revokedTokens;

    public MongoCorePersistenceTests(MongoDbContainerFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
                ["Mongo:DatabaseName"] = $"anxietywatch_tests_{Guid.NewGuid():N}"
            })
            .Build();
        context = new MongoContext(configuration);
        users = new MongoUserRepository(context);
        tokens = new MongoLinkTokenRepository(context);
        revokedTokens = new MongoRevokedTokenStore(context);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task UserRepository_ShouldRejectStaleWritesAndLockConcurrentFailures()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var user = new User(Guid.NewGuid(), "Mongo User", "mongo-user@example.test", "hash", "family");
        await users.AddAsync(user);

        var first = await users.GetByIdAsync(user.Id);
        var stale = await users.GetByEmailAsync(user.Email);
        first!.UpdateProfile("Updated User", null);
        await users.UpdateAsync(first);
        stale!.UpdateSettings(80, false, true);

        await FluentActions.Invoking(() => users.UpdateAsync(stale))
            .Should().ThrowAsync<ConflictException>();

        await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => users.RegisterFailedLoginAsync(user.Id, now, "hash")));
        var locked = await users.GetByIdAsync(user.Id);
        locked!.FailedLoginAttempts.Should().Be(5);
        locked.IsLockedOut(now).Should().BeTrue();
        var lockoutUntil = locked.LockoutUntil;
        await users.RegisterFailedLoginAsync(user.Id, now.AddSeconds(-10), "hash");
        locked = await users.GetByIdAsync(user.Id);
        locked!.LockoutUntil.Should().Be(lockoutUntil);
        (await users.RegisterSuccessfulLoginAsync(
            user.Id,
            now,
            locked.Version,
            locked.PasswordHash)).Should().BeNull();
        (await users.RegisterSuccessfulLoginAsync(
            user.Id,
            now.AddSeconds(61),
            locked.Version,
            locked.PasswordHash))!.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task UserRepository_ShouldUpgradeDocumentsWithoutVersion()
    {
        var id = Guid.NewGuid();
        await context.Database.GetCollection<BsonDocument>("users").InsertOneAsync(new BsonDocument
        {
            ["_id"] = id.ToString(),
            ["fullName"] = "Legacy User",
            ["email"] = "legacy@example.test",
            ["passwordHash"] = "hash",
            ["planId"] = "free"
        });

        var legacy = await users.GetByIdAsync(id);
        legacy!.Version.Should().Be(0);
        legacy.SecurityVersion.Should().Be(0);
        legacy.UpdateProfile("Updated Legacy User", null);
        await users.UpdateAsync(legacy);

        legacy.Version.Should().Be(1);
        (await users.GetByIdAsync(id))!.FullName.Should().Be("Updated Legacy User");
        (await users.UpdatePasswordAsync(id, "new-hash")).Should().BeTrue();
        (await users.GetByIdAsync(id))!.SecurityVersion.Should().Be(1);
    }

    [Fact]
    public async Task EmailVerification_ShouldReplaceExpireAndConsumeTokensAtomically()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User(Guid.NewGuid(), "Verification User", "verify@example.test", "hash", "free");
        await users.AddAsync(user);
        var stored = await users.GetByIdAsync(user.Id);

        (await users.StoreEmailVerificationTokenAsync(
            user.Id,
            now,
            "old-token-hash",
            now.AddHours(24),
            stored!.Version)).Should().NotBeNull();
        stored = await users.GetByIdAsync(user.Id);
        (await users.StoreEmailVerificationTokenAsync(
            user.Id,
            now.AddMinutes(1),
            "current-token-hash",
            now.AddHours(24),
            stored!.Version)).Should().NotBeNull();

        (await users.ConfirmEmailAsync("old-token-hash", now)).Should().BeFalse();
        var confirmations = await Task.WhenAll(
            users.ConfirmEmailAsync("current-token-hash", now),
            users.ConfirmEmailAsync("current-token-hash", now));
        confirmations.Should().ContainSingle(result => result);
        confirmations.Should().ContainSingle(result => !result);
        (await users.GetByIdAsync(user.Id))!.EmailVerified.Should().BeTrue();

        var document = await context.Database.GetCollection<BsonDocument>("users")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", user.Id.ToString()))
            .SingleAsync();
        document.Contains("emailVerificationTokenHash").Should().BeFalse();
        document.Contains("emailVerificationTokenExpiresAt").Should().BeFalse();

        var expiredUser = new User(Guid.NewGuid(), "Expired User", "expired@example.test", "hash", "free");
        await users.AddAsync(expiredUser);
        (await users.StoreEmailVerificationTokenAsync(
            expiredUser.Id,
            now.AddDays(-2),
            "expired-token-hash",
            now.AddMinutes(-1),
            0)).Should().NotBeNull();
        (await users.ConfirmEmailAsync("expired-token-hash", now)).Should().BeFalse();

        var rollbackUser = new User(Guid.NewGuid(), "Rollback User", "rollback@example.test", "hash", "free");
        await users.AddAsync(rollbackUser);
        await users.StoreEmailVerificationTokenAsync(
            rollbackUser.Id,
            now.AddMinutes(-2),
            "delivered-token-hash",
            now.AddHours(23),
            0);
        var rollbackSnapshot = await users.GetByIdAsync(rollbackUser.Id);
        var previousState = await users.StoreEmailVerificationTokenAsync(
            rollbackUser.Id,
            now,
            "failed-token-hash",
            now.AddHours(24),
            rollbackSnapshot!.Version);
        await users.RollbackEmailVerificationTokenAsync(
            rollbackUser.Id,
            "failed-token-hash",
            now,
            previousState!);

        (await users.ConfirmEmailAsync("failed-token-hash", now)).Should().BeFalse();
        (await users.ConfirmEmailAsync("delivered-token-hash", now)).Should().BeTrue();
    }

    [Fact]
    public async Task LinkTokenRepository_ShouldEnforceConcurrentQuotaAndConditionalStates()
    {
        var ownerId = Guid.NewGuid();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            tokens.TryAddAsync(NewLinkToken(ownerId, $"AW-CONCURRENT-{index:00}"), 5)));
        attempts.Count(result => result).Should().Be(5);
        (await tokens.GetAsync(ownerId)).Should().HaveCount(5);

        var acceptedToken = (await tokens.GetAsync(ownerId))[0];
        var staleCopy = await tokens.GetByIdAsync(acceptedToken.Id);
        acceptedToken.Accept(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await tokens.UpdateAsync(acceptedToken);
        staleCopy!.MarkDeleted();
        await FluentActions.Invoking(() => tokens.UpdateAsync(staleCopy))
            .Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task LinkTokenRepository_ShouldCountLegacyDocumentsAgainstQuota()
    {
        var ownerId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        await context.Database.GetCollection<BsonDocument>("link_tokens").InsertOneAsync(new BsonDocument
        {
            ["_id"] = legacyId.ToString(),
            ["userId"] = ownerId.ToString(),
            ["code"] = "AW-LEGACY-TOKEN",
            ["role"] = "family_member",
            ["expiresAt"] = new BsonDateTime(DateTime.UtcNow.AddDays(30)),
            ["status"] = TokenStatus.Pending.ToString(),
            ["acceptedBy"] = BsonNull.Value,
            ["acceptedAt"] = BsonNull.Value
        });

        (await tokens.TryAddAsync(NewLinkToken(ownerId, "AW-NEW-BLOCKED"), 1)).Should().BeFalse();
        var legacy = await tokens.GetByIdAsync(legacyId);
        legacy!.MarkDeleted();
        await tokens.UpdateAsync(legacy);
        (await tokens.TryAddAsync(NewLinkToken(ownerId, "AW-NEW-ALLOWED"), 1)).Should().BeTrue();
    }

    [Fact]
    public async Task RevokedTokenStore_ShouldKeepTheLongestExpirationAcrossConcurrentWrites()
    {
        var jwtId = Guid.NewGuid().ToString("N");
        var longExpiration = DateTimeOffset.UtcNow.AddMinutes(20);
        await Task.WhenAll(
            revokedTokens.RevokeAsync(jwtId, longExpiration),
            revokedTokens.RevokeAsync(jwtId, longExpiration.AddMinutes(-10)));

        var document = await context.Database.GetCollection<BsonDocument>("revoked_tokens")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", jwtId))
            .SingleAsync();
        new DateTimeOffset(document["expiresAt"].ToUniversalTime())
            .Should().BeCloseTo(longExpiration, TimeSpan.FromMilliseconds(1));
    }

    private static LinkToken NewLinkToken(Guid userId, string code) =>
        new(Guid.NewGuid(), userId, code, "family_member", DateTimeOffset.UtcNow.AddDays(30));
}
