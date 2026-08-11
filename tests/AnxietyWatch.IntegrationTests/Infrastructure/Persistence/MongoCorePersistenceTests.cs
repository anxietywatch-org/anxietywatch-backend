using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Episodes;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AnxietyWatch.IntegrationTests.Infrastructure.Persistence;

public sealed class MongoCorePersistenceTests : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context;
    private readonly MongoUserRepository users;
    private readonly MongoEpisodeRepository episodes;
    private readonly MongoLinkTokenRepository tokens;
    private readonly MongoRevokedTokenStore revokedTokens;
    private readonly MongoPasswordResetTokenStore resetTokens;

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
        episodes = new MongoEpisodeRepository(context);
        tokens = new MongoLinkTokenRepository(context);
        revokedTokens = new MongoRevokedTokenStore(context);
        resetTokens = new MongoPasswordResetTokenStore(context);
    }

    public Task InitializeAsync() => new MongoIndexInitializer(context).StartAsync(CancellationToken.None);

    public Task DisposeAsync() => context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task UserRepository_ShouldRoundTripStateAndRejectStaleUpdatesAndDuplicateEmail()
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var user = User.Rehydrate(
            Guid.NewGuid(),
            "Mongo User",
            "mongo-user@example.test",
            "password-hash",
            "family",
            true,
            now.AddMinutes(-5),
            "https://example.test/avatar.png",
            55,
            false,
            true,
            4,
            now.AddSeconds(-30),
            now.AddSeconds(30),
            0);
        await users.AddAsync(user);

        var first = await users.GetByIdAsync(user.Id);
        var stale = await users.GetByEmailAsync(user.Email);

        first.Should().BeEquivalentTo(user);
        first!.UpdateProfile("Updated Mongo User", null);
        await users.UpdateAsync(first);
        stale!.UpdateSettings(80, true, false);
        await FluentActions.Invoking(() => users.UpdateAsync(stale))
            .Should().ThrowAsync<ConflictException>();
        (await users.GetByIdAsync(user.Id))!.FullName.Should().Be("Updated Mongo User");

        var duplicate = new User(Guid.NewGuid(), "Duplicate", user.Email, "hash", "free");
        await FluentActions.Invoking(() => users.AddAsync(duplicate))
            .Should().ThrowAsync<ConflictException>();

        var loginUser = new User(Guid.NewGuid(), "Login User", "login@example.test", "hash", "free");
        await users.AddAsync(loginUser);
        await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => users.RegisterFailedLoginAsync(loginUser.Id, now)));
        var lockedUser = await users.GetByIdAsync(loginUser.Id);
        lockedUser!.FailedLoginAttempts.Should().Be(5);
        lockedUser.IsLockedOut(now).Should().BeTrue();
        (await users.RegisterSuccessfulLoginAsync(loginUser.Id, now, 0, "hash")).Should().BeNull();
        (await users.RegisterSuccessfulLoginAsync(
            loginUser.Id,
            now,
            lockedUser.Version,
            lockedUser.PasswordHash)).Should().BeNull();
        (await users.RegisterSuccessfulLoginAsync(
            loginUser.Id,
            now.AddSeconds(61),
            lockedUser.Version,
            lockedUser.PasswordHash))!.FailedLoginAttempts.Should().Be(0);

        (await users.UpdatePasswordAsync(user.Id, "new-password-hash")).Should().BeTrue();
        (await users.GetByIdAsync(user.Id))!.PasswordHash.Should().Be("new-password-hash");
    }

    [Fact]
    public async Task EpisodeRepository_ShouldFilterCountAndOrderEpisodes()
    {
        var userId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await episodes.AddAsync(new Episode(Guid.NewGuid(), userId, now.AddDays(-2), 20, ["old"], null));
        await episodes.AddAsync(new Episode(Guid.NewGuid(), userId, now.AddHours(-1), 80, ["recent"], "note"));
        await episodes.AddAsync(new Episode(Guid.NewGuid(), Guid.NewGuid(), now, 10, [], null));

        var result = await episodes.GetAsync(userId, now.AddDays(-1));

        result.Should().ContainSingle();
        result[0].Intensity.Should().Be(80);
        result[0].Symptoms.Should().ContainSingle("recent");
        (await episodes.CountAsync(userId, now.AddDays(-3))).Should().Be(2);
    }

    [Fact]
    public async Task LinkTokenRepository_ShouldEnforceQuotaAndRoundTripAcceptedState()
    {
        var ownerId = Guid.NewGuid();
        var first = NewLinkToken(ownerId, "AW-MONGO-FIRST");
        var second = NewLinkToken(ownerId, "AW-MONGO-SECOND");

        (await tokens.TryAddAsync(first, 1)).Should().BeTrue();
        (await tokens.TryAddAsync(second, 1)).Should().BeFalse();

        first.MarkDeleted();
        await tokens.UpdateAsync(first);
        (await tokens.TryAddAsync(second, 1)).Should().BeTrue();

        var acceptedBy = Guid.NewGuid();
        var acceptedAt = DateTimeOffset.UtcNow;
        second.Accept(acceptedBy, acceptedAt);
        await tokens.UpdateAsync(second);

        var stored = await tokens.GetByIdAsync(second.Id);
        stored!.Status.Should().Be(TokenStatus.Accepted);
        stored.AcceptedBy.Should().Be(acceptedBy);
        stored.AcceptedAt.Should().BeCloseTo(acceptedAt, TimeSpan.FromMilliseconds(1));
        stored.MarkDeleted();
        await FluentActions.Invoking(() => tokens.UpdateAsync(stored))
            .Should().ThrowAsync<ConflictException>();

        var reducedOwner = Guid.NewGuid();
        var highSlotFirst = NewLinkToken(reducedOwner, "AW-REDUCE-FIRST");
        var highSlotSecond = NewLinkToken(reducedOwner, "AW-REDUCE-SECOND");
        (await tokens.TryAddAsync(highSlotFirst, 5)).Should().BeTrue();
        (await tokens.TryAddAsync(highSlotSecond, 5)).Should().BeTrue();
        highSlotFirst.MarkDeleted();
        await tokens.UpdateAsync(highSlotFirst);
        (await tokens.TryAddAsync(NewLinkToken(reducedOwner, "AW-REDUCE-THIRD"), 1)).Should().BeFalse();
    }

    [Fact]
    public async Task SecurityStores_ShouldPersistRevocationsAndConsumeResetTokensOnce()
    {
        var jwtId = Guid.NewGuid().ToString("N");
        var expiration = DateTimeOffset.UtcNow.AddMinutes(10);
        await revokedTokens.RevokeAsync(jwtId, expiration);
        await revokedTokens.RevokeAsync(jwtId, expiration.AddMinutes(-5));
        (await revokedTokens.IsRevokedAsync(jwtId)).Should().BeTrue();

        var tokenHash = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid();
        await resetTokens.StoreAsync(tokenHash, userId, DateTimeOffset.UtcNow.AddMinutes(5));
        var consumptions = await Task.WhenAll(
            resetTokens.ConsumeAsync(tokenHash, DateTimeOffset.UtcNow),
            resetTokens.ConsumeAsync(tokenHash, DateTimeOffset.UtcNow));
        consumptions.Should().ContainSingle(value => value == userId);
        consumptions.Should().ContainSingle(value => value == null);
    }

    private static LinkToken NewLinkToken(Guid userId, string code) =>
        new(Guid.NewGuid(), userId, code, "family_member", DateTimeOffset.UtcNow.AddDays(30));
}
