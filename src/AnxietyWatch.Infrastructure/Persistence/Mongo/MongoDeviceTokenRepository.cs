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

    public async Task<bool> TryUpsertAsync(DeviceToken device, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("token", device.Token);
        await Collection.ReplaceOneAsync(
            filter,
            Map(device),
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
        return true;
    }

    public async Task<bool> TryDeleteAsync(Guid userId, string token, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("token", token),
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()));
        var result = await Collection.DeleteOneAsync(filter, cancellationToken);
        return result.DeletedCount == 1;
    }

    private static BsonDocument Map(DeviceToken device) => new()
    {
        ["_id"] = device.Id.ToString(),
        ["userId"] = device.UserId.ToString(),
        ["platform"] = device.Platform,
        ["token"] = device.Token,
        ["createdAt"] = Date(device.CreatedAt),
        ["updatedAt"] = Date(device.UpdatedAt)
    };

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