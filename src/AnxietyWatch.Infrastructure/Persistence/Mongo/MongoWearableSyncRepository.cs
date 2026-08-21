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
    private readonly IMongoCollection<BsonDocument> suspectedEvents = context.Database.GetCollection<BsonDocument>("suspected_events");
    private readonly IMongoCollection<BsonDocument> eventDecisions = context.Database.GetCollection<BsonDocument>("event_decisions");

    public Task<bool> TryStoreTelemetryAsync(Guid userId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default) =>
        TryInsertAsync(telemetry, batch.BatchId, userId, batch, cancellationToken);

    public Task<bool> TryStoreSosAsync(Guid userId, SosTriggerRequest trigger, CancellationToken cancellationToken = default) =>
        TryInsertAsync(sosEvents, trigger.EventId, userId, trigger, cancellationToken);

    public Task<bool> TryStoreSosCancellationAsync(Guid userId, SosCancelRequest cancellation, CancellationToken cancellationToken = default) =>
        TryInsertAsync(sosCancellations, cancellation.EventId, userId, cancellation, cancellationToken);

    public Task<bool> TryStoreSuspectedEventAsync(Guid userId, SuspectedEventRequest suspectedEvent, CancellationToken cancellationToken = default) =>
        TryInsertAsync(suspectedEvents, suspectedEvent.EventId, userId, suspectedEvent, cancellationToken);

    public Task<bool> TryStoreEventDecisionAsync(Guid userId, EventDecisionRequest decision, CancellationToken cancellationToken = default) =>
        TryInsertAsync(eventDecisions, decision.EventId, userId, decision, cancellationToken);

    public async Task<TelemetryWindowResult> GetTelemetryWindowAsync(
        Guid userId,
        Guid deviceId,
        Guid sessionId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()),
            Builders<BsonDocument>.Filter.Eq("DeviceId", deviceId.ToString()),
            Builders<BsonDocument>.Filter.Eq("SessionId", sessionId.ToString()));

        var batches = new List<TelemetryBatchRequest>();
        using var cursor = await telemetry.Find(filter).ToCursorAsync(cancellationToken);
        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var document in cursor.Current)
            {
                batches.Add(JsonSerializer.Deserialize<TelemetryBatchRequest>(document.ToJson())!);
            }
        }

        return TelemetryWindowSelector.Select(batches, windowStart, windowEnd);
    }

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
