using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoEventInferencePersistenceTests : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context;
    private readonly MongoEventInferenceRepository repository;

    public MongoEventInferencePersistenceTests(MongoDbContainerFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
                ["Mongo:DatabaseName"] = $"anxietywatch_tests_{Guid.NewGuid():N}"
            })
            .Build();
        context = new MongoContext(configuration);
        repository = new MongoEventInferenceRepository(context);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() =>
        context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task StoredOnceAndIdempotent_WithEventIdAsNaturalKey()
    {
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var result = new EventInferenceResult(
            eventId,
            EventInferenceStatus.Succeeded,
            1,
            0.95,
            0.3,
            "v0.1.0",
            "target_support_requested",
            null,
            DateTimeOffset.UtcNow);

        (await repository.TryStoreInferenceAsync(userId, result)).Should().BeTrue();
        (await repository.TryStoreInferenceAsync(userId, result)).Should().BeFalse();

        var collection = context.Database.GetCollection<BsonDocument>("event_inferences");
        var documents = await collection.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
        documents.Should().ContainSingle();
        documents[0]["_id"].AsString.Should().Be(eventId.ToString());
        documents[0]["userId"].AsString.Should().Be(userId.ToString());
        documents[0]["Status"].AsString.Should().Be("Succeeded");
        documents[0]["Prediction"].AsInt32.Should().Be(1);
        documents[0]["ModelVersion"].AsString.Should().Be("v0.1.0");
        documents[0]["Target"].AsString.Should().Be("target_support_requested");
        documents[0]["FailureKind"].IsBsonNull.Should().BeTrue();

        var stored = (await repository.GetInferenceAsync(eventId))!;
        stored.Status.Should().Be(EventInferenceStatus.Succeeded);
        stored.Prediction.Should().Be(1);
    }

    [Fact]
    public async Task DocumentStoresNoRawTelemetryOrSecrets()
    {
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var result = new EventInferenceResult(
            eventId,
            EventInferenceStatus.Failed,
            null,
            null,
            null,
            null,
            null,
            AnxietyWatch.Application.Abstractions.MlInference.MlInferenceFailureKind.Transient,
            DateTimeOffset.UtcNow);
        (await repository.TryStoreInferenceAsync(userId, result)).Should().BeTrue();

        var collection = context.Database.GetCollection<BsonDocument>("event_inferences");
        var document = (await collection.Find(Builders<BsonDocument>.Filter.Empty).FirstOrDefaultAsync())!;

        document.Contains("samples").Should().BeFalse();
        document.Contains("heartRate").Should().BeFalse();
        document.Contains("heartRateBpm").Should().BeFalse();
        document.Contains("ibiMs").Should().BeFalse();
        document.Contains("skinTemperature").Should().BeFalse();
        document.Contains("apiKey").Should().BeFalse();
        document.Contains("ApiKey").Should().BeFalse();
        document.Contains("request").Should().BeFalse();
        document.Contains("response").Should().BeFalse();
        document["Status"].AsString.Should().Be("Failed");
        document["FailureKind"].AsString.Should().Be("Transient");
    }
}