using AnxietyWatch.Domain.Episodes;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoEpisodeRepository(MongoContext context) : IEpisodeRepository
{
    private IMongoCollection<BsonDocument> Collection =>
        context.Database.GetCollection<BsonDocument>("episodes");

    public async Task<IReadOnlyList<Episode>> GetAsync(
        Guid userId,
        DateTimeOffset from,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()),
            Builders<BsonDocument>.Filter.Gte("date", new BsonDateTime(from.UtcDateTime)));

        var documents = await Collection.Find(filter)
            .SortByDescending(document => document["date"])
            .ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<int> CountAsync(Guid userId, DateTimeOffset from, CancellationToken cancellationToken = default)
    {
        var count = await Collection.CountDocumentsAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()),
                Builders<BsonDocument>.Filter.Gte("date", new BsonDateTime(from.UtcDateTime))),
            cancellationToken: cancellationToken);
        return checked((int)count);
    }

    public Task AddAsync(Episode episode, CancellationToken cancellationToken = default) =>
        Collection.InsertOneAsync(Map(episode), cancellationToken: cancellationToken);

    private static BsonDocument Map(Episode episode) => new()
    {
        ["_id"] = episode.Id.ToString(),
        ["userId"] = episode.UserId.ToString(),
        ["date"] = new BsonDateTime(episode.Date.UtcDateTime),
        ["intensity"] = episode.Intensity,
        ["symptoms"] = new BsonArray(episode.Symptoms),
        ["notes"] = episode.Notes is null ? BsonNull.Value : episode.Notes
    };

    private static Episode Map(BsonDocument document) => new(
        Guid.Parse(document["_id"].AsString),
        Guid.Parse(document["userId"].AsString),
        new DateTimeOffset(document["date"].ToUniversalTime()),
        document["intensity"].ToInt32(),
        document.GetValue("symptoms", new BsonArray()).AsBsonArray.Select(value => value.AsString).ToArray(),
        document.TryGetValue("notes", out var notes) && !notes.IsBsonNull ? notes.AsString : null);
}
