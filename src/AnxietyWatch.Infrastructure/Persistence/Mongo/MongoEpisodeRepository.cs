using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Episodes;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoEpisodeRepository(MongoContext context) : IEpisodeRepository
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("episodes");

    public async Task<IReadOnlyList<Episode>> GetAsync(
        Guid userId,
        DateTimeOffset from,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()),
            Builders<BsonDocument>.Filter.Gte("date", MongoDocument.Date(from)));
        var documents = await Collection.Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("date"))
            .ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<int> CountAsync(Guid userId, DateTimeOffset from, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()),
            Builders<BsonDocument>.Filter.Gte("date", MongoDocument.Date(from)));
        var count = await Collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return checked((int)count);
    }

    public async Task AddAsync(Episode episode, CancellationToken cancellationToken = default)
    {
        var document = new BsonDocument
        {
            ["_id"] = episode.Id.ToString(),
            ["userId"] = episode.UserId.ToString(),
            ["date"] = MongoDocument.Date(episode.Date),
            ["intensity"] = episode.Intensity,
            ["symptoms"] = new BsonArray(episode.Symptoms),
            ["notes"] = MongoDocument.NullableString(episode.Notes)
        };

        try
        {
            await Collection.InsertOneAsync(document, cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new ConflictException("The episode already exists.");
        }
    }

    private static Episode Map(BsonDocument document) => new(
        Guid.Parse(document["_id"].AsString),
        Guid.Parse(document["userId"].AsString),
        MongoDocument.ReadDate(document["date"]),
        document["intensity"].AsInt32,
        document["symptoms"].AsBsonArray.Select(value => value.AsString).ToArray(),
        MongoDocument.ReadNullableString(document, "notes"));
}
