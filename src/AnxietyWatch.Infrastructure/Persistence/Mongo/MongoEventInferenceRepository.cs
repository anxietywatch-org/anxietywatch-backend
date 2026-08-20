using System.Text.Json;
using AnxietyWatch.Application.Features.Wearables;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoEventInferenceRepository(MongoContext context) : IEventInferenceRepository
{
    private readonly IMongoCollection<BsonDocument> inferences =
        context.Database.GetCollection<BsonDocument>("event_inferences");

    public async Task<bool> TryStoreInferenceAsync(
        Guid userId,
        EventInferenceResult result,
        CancellationToken cancellationToken = default)
    {
        var document = BsonDocument.Parse(JsonSerializer.Serialize(result));
        document["_id"] = result.EventId.ToString();
        document["userId"] = userId.ToString();

        try
        {
            await inferences.InsertOneAsync(document, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<EventInferenceResult?> GetInferenceAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", eventId.ToString());
        var document = await inferences.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document is null
            ? null
            : JsonSerializer.Deserialize<EventInferenceResult>(document.ToJson());
    }
}