using AnxietyWatch.Domain.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoLinkTokenRepository(MongoContext context) : ILinkTokenRepository
{
    private IMongoCollection<BsonDocument> Collection =>
        context.Database.GetCollection<BsonDocument>("link_tokens");

    public async Task<IReadOnlyList<LinkToken>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()),
            Builders<BsonDocument>.Filter.Ne("status", TokenStatus.Deleted.ToString()));
        var documents = await Collection.Find(filter)
            .SortByDescending(document => document["expiresAt"])
            .ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<bool> TryAddAsync(LinkToken token, int maximum, CancellationToken cancellationToken = default)
    {
        var activeFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("userId", token.UserId.ToString()),
            Builders<BsonDocument>.Filter.Ne("status", TokenStatus.Deleted.ToString()));
        var activeCount = await Collection.CountDocumentsAsync(activeFilter, cancellationToken: cancellationToken);
        if (activeCount >= maximum)
        {
            return false;
        }

        try
        {
            await Collection.InsertOneAsync(Map(token), cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<LinkToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await Collection.Find(Builders<BsonDocument>.Filter.Eq("_id", id.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public Task UpdateAsync(LinkToken token, CancellationToken cancellationToken = default) =>
        Collection.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", token.Id.ToString()),
            Map(token),
            cancellationToken: cancellationToken);

    private static BsonDocument Map(LinkToken token) => new()
    {
        ["_id"] = token.Id.ToString(),
        ["userId"] = token.UserId.ToString(),
        ["code"] = token.Code,
        ["role"] = token.Role,
        ["expiresAt"] = new BsonDateTime(token.ExpiresAt.UtcDateTime),
        ["status"] = token.Status.ToString(),
        ["acceptedBy"] = token.AcceptedBy is null ? BsonNull.Value : token.AcceptedBy.Value.ToString(),
        ["acceptedAt"] = token.AcceptedAt is null ? BsonNull.Value : new BsonDateTime(token.AcceptedAt.Value.UtcDateTime)
    };

    private static LinkToken Map(BsonDocument document) => LinkToken.Restore(
        Guid.Parse(document["_id"].AsString),
        Guid.Parse(document["userId"].AsString),
        document["code"].AsString,
        document["role"].AsString,
        new DateTimeOffset(document["expiresAt"].ToUniversalTime()),
        Enum.Parse<TokenStatus>(document.GetValue("status", TokenStatus.Pending.ToString()).AsString),
        document.TryGetValue("acceptedBy", out var acceptedBy) && !acceptedBy.IsBsonNull ? Guid.Parse(acceptedBy.AsString) : null,
        document.TryGetValue("acceptedAt", out var acceptedAt) && !acceptedAt.IsBsonNull
            ? new DateTimeOffset(acceptedAt.ToUniversalTime())
            : null);
}
