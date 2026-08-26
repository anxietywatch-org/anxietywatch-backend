using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoDeviceRegistrationTests : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context;
    private readonly MongoDeviceTokenRepository devices;

    public MongoDeviceRegistrationTests(MongoDbContainerFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
                ["Mongo:DatabaseName"] = $"anxietywatch_device_tests_{Guid.NewGuid():N}"
            })
            .Build();
        context = new MongoContext(configuration);
        devices = new MongoDeviceTokenRepository(context);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() =>
        context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task OwnershipTransferIsAtomicAndMultipleTokensCoexist()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var first = await devices.UpsertAsync(Device(firstUser, "shared", "android"));

        var transferred = await devices.UpsertAsync(Device(secondUser, "shared", "ios"));
        await devices.UpsertAsync(Device(secondUser, "second", "android"));

        transferred.Id.Should().Be(first.Id);
        transferred.CreatedAt.Should().Be(first.CreatedAt);
        transferred.UserId.Should().Be(secondUser);
        transferred.Platform.Should().Be("ios");
        (await devices.GetForUserAsync(firstUser)).Should().BeEmpty();
        (await devices.GetForUserAsync(secondUser)).Should().HaveCount(2);
    }

    [Fact]
    public async Task ConcurrentFirstRegistrationLeavesOneTokenWithOneFinalOwner()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();

        await Task.WhenAll(
            devices.UpsertAsync(Device(firstUser, "race-token", "android")),
            devices.UpsertAsync(Device(secondUser, "race-token", "ios")));

        var documents = await context.Database.GetCollection<BsonDocument>("device_tokens")
            .Find(Builders<BsonDocument>.Filter.Eq("token", "race-token"))
            .ToListAsync();
        documents.Should().ContainSingle();
        new[] { firstUser.ToString(), secondUser.ToString() }
            .Should().Contain(documents[0]["userId"].AsString);
    }

    [Fact]
    public async Task StartupCreatesUniqueTokenAndUserLookupIndexes()
    {
        using var cursor = await context.Database.GetCollection<BsonDocument>("device_tokens")
            .Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();

        indexes.Should().Contain(index =>
            index.GetValue("unique", false).ToBoolean() &&
            index["key"].AsBsonDocument.Contains("token"));
        indexes.Should().Contain(index => index["key"].AsBsonDocument.Contains("userId"));
    }

    private static DeviceToken Device(Guid userId, string token, string platform) =>
        new(Guid.NewGuid(), userId, platform, token, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
}
