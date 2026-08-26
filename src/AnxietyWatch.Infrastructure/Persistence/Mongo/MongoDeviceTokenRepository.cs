using AnxietyWatch.Domain.Devices;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoDeviceTokenRepository(MongoContext context) : IDeviceTokenRepository
{
    private IMongoCollection<BsonDocument> Collection =>
        context.Database.GetCollection<BsonDocument>("device_tokens");

    public async Task<IReadOnlyList<DeviceToken>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var documents = await Collection.Find(Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()))
            .SortBy(document => document["createdAt"])
            .ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("token", token))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<DeviceToken> UpsertAsync(DeviceToken device, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("token", device.Token);
        var update = Builders<BsonDocument>.Update
            .Set("userId", device.UserId.ToString())
            .Set("platform", device.Platform)
            .Set("updatedAt", Date(device.UpdatedAt))
            .SetOnInsert("_id", device.Id.ToString())
            .SetOnInsert("token", device.Token)
            .SetOnInsert("createdAt", Date(device.CreatedAt));

        BsonDocument? document;
        try
        {
            document = await Collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<BsonDocument>
                {
                    IsUpsert = true,
                    ReturnDocument = ReturnDocument.After
                },
                cancellationToken);
        }
        catch (MongoWriteException exception) when (
            exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A concurrent first registration won the unique-token insert.
            // Retry as an update so the token still has exactly one final owner.
            document = await Collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
                cancellationToken);
        }
        catch (MongoCommandException exception) when (exception.Code == 11000)
        {
            document = await Collection.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
                cancellationToken);
        }

        return Map(document ?? throw new InvalidOperationException("The device registration could not be persisted."));
    }

    public async Task<bool> TryDeleteAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("token", token),
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()));
        var result = await Collection.DeleteOneAsync(filter, cancellationToken);
        return result.DeletedCount == 1;
    }

    private static DeviceToken Map(BsonDocument document) => DeviceToken.Restore(
        Guid.Parse(document["_id"].AsString),
        Guid.Parse(document["userId"].AsString),
        document["platform"].AsString,
        document["token"].AsString,
        new DateTimeOffset(document["createdAt"].ToUniversalTime()),
        document.TryGetValue("updatedAt", out var updatedAt) && !updatedAt.IsBsonNull
            ? new DateTimeOffset(updatedAt.ToUniversalTime())
            : new DateTimeOffset(document["createdAt"].ToUniversalTime()));

    private static BsonDateTime Date(DateTimeOffset value) => new(value.UtcDateTime);
}
