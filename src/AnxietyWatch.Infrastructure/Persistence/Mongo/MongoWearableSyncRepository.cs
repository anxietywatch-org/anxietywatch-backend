using System.Text.Json;
using AnxietyWatch.Application.Features.Wearables;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoWearableSyncRepository(MongoContext context) : IWearableSyncRepository
{
    private readonly IMongoCollection<BsonDocument> telemetry = context.Database.GetCollection<BsonDocument>("telemetry_batches");
    private readonly IMongoCollection<BsonDocument> sosEvents = context.Database.GetCollection<BsonDocument>("sos_events");
    private readonly IMongoCollection<BsonDocument> sosCancellations = context.Database.GetCollection<BsonDocument>("sos_cancellations");

    public Task<bool> TryStoreTelemetryAsync(Guid userId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default) =>
        TryInsertAsync(telemetry, batch.BatchId, userId, batch, cancellationToken);

    public Task<bool> TryStoreSosAsync(Guid userId, SosTriggerRequest trigger, CancellationToken cancellationToken = default) =>
        TryInsertAsync(sosEvents, trigger.EventId, userId, trigger, cancellationToken);

    public Task<bool> TryStoreSosCancellationAsync(Guid userId, SosCancelRequest cancellation, CancellationToken cancellationToken = default) =>
        TryInsertAsync(sosCancellations, cancellation.EventId, userId, cancellation, cancellationToken);

    private static async Task<bool> TryInsertAsync<T>(
        IMongoCollection<BsonDocument> collection,
        Guid id,
        Guid userId,
        T payload,
        CancellationToken cancellationToken)
    {
        var document = BsonDocument.Parse(JsonSerializer.Serialize(payload));
        document["_id"] = id.ToString();
        document["userId"] = userId.ToString();
        document["receivedAt"] = DateTimeOffset.UtcNow.ToString("O");

        try
        {
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
