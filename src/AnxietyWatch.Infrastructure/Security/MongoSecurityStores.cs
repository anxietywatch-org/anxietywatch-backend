using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class MongoRevokedTokenStore(MongoContext context) : IRevokedTokenStore
{
    private readonly IMongoCollection<BsonDocument> revokedTokens = context.Database.GetCollection<BsonDocument>("revoked_tokens");

    public async Task RevokeAsync(
        string jwtId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", jwtId);
        var update = Builders<BsonDocument>.Update
            .SetOnInsert("_id", jwtId)
            .Max("expiresAt", new BsonDateTime(expiresAt.UtcDateTime));
        try
        {
            await revokedTokens.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await revokedTokens.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        }
    }

    public async Task<bool> IsRevokedAsync(string jwtId, CancellationToken cancellationToken = default) =>
        await revokedTokens.Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", jwtId),
                Builders<BsonDocument>.Filter.Gt("expiresAt", new BsonDateTime(DateTime.UtcNow))))
            .AnyAsync(cancellationToken);
}

public sealed class MongoPasswordResetTokenStore(MongoContext context) : IPasswordResetTokenStore
{
    private readonly IMongoCollection<BsonDocument> resetTokens = context.Database.GetCollection<BsonDocument>("password_reset_tokens");

    public Task StoreAsync(string tokenHash, Guid userId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        resetTokens.ReplaceOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", tokenHash),
            new BsonDocument
            {
                ["_id"] = tokenHash,
                ["userId"] = userId.ToString(),
                ["expiresAt"] = new BsonDateTime(expiresAt.UtcDateTime)
            },
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);

    public async Task<Guid?> ConsumeAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var document = await resetTokens.FindOneAndDeleteAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", tokenHash),
                Builders<BsonDocument>.Filter.Gt("expiresAt", new BsonDateTime(now.UtcDateTime))),
            cancellationToken: cancellationToken);
        return document is null ? null : Guid.Parse(document["userId"].AsString);
    }
}
