using AnxietyWatch.Application.Abstractions.Security;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoRevokedTokenStore(MongoContext context) : IRevokedTokenStore
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("revoked_jwts");

    public async Task RevokeAsync(
        string jwtId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", jwtId);
        var update = Builders<BsonDocument>.Update.Max("expiresAt", MongoDocument.Date(expiresAt));
        try
        {
            await Collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await Collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        }
    }

    public async Task<bool> IsRevokedAsync(string jwtId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", jwtId),
            Builders<BsonDocument>.Filter.Gt("expiresAt", MongoDocument.Date(DateTimeOffset.UtcNow)));
        return await Collection.Find(filter).AnyAsync(cancellationToken);
    }
}

public sealed class MongoPasswordResetTokenStore(MongoContext context) : IPasswordResetTokenStore
{
    private IMongoCollection<BsonDocument> Collection =>
        context.Database.GetCollection<BsonDocument>("password_reset_tokens");

    public Task StoreAsync(
        string tokenHash,
        Guid userId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var document = new BsonDocument
        {
            ["_id"] = tokenHash,
            ["userId"] = userId.ToString(),
            ["expiresAt"] = MongoDocument.Date(expiresAt)
        };
        return Collection.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", tokenHash),
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<Guid?> ConsumeAsync(
        string tokenHash,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("_id", tokenHash),
            Builders<BsonDocument>.Filter.Gt("expiresAt", MongoDocument.Date(now)));
        var document = await Collection.FindOneAndDeleteAsync(filter, cancellationToken: cancellationToken);
        return document is null ? null : Guid.Parse(document["userId"].AsString);
    }
}
