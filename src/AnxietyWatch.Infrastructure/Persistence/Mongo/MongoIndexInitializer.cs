using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoIndexInitializer(MongoContext context) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await CreateUsersIndexes(cancellationToken);
        await CreateEpisodeIndexes(cancellationToken);
        await CreateLinkTokenIndexes(cancellationToken);
        await CreateSecurityIndexes(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Task CreateUsersIndexes(CancellationToken cancellationToken)
    {
        var collection = context.Database.GetCollection<BsonDocument>("users");
        var index = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("email"),
            new CreateIndexOptions { Name = "ux_users_email", Unique = true });
        return collection.Indexes.CreateOneAsync(index, cancellationToken: cancellationToken);
    }

    private Task CreateEpisodeIndexes(CancellationToken cancellationToken)
    {
        var collection = context.Database.GetCollection<BsonDocument>("episodes");
        var index = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("date"),
            new CreateIndexOptions { Name = "ix_episodes_user_date" });
        return collection.Indexes.CreateOneAsync(index, cancellationToken: cancellationToken);
    }

    private Task CreateLinkTokenIndexes(CancellationToken cancellationToken)
    {
        var collection = context.Database.GetCollection<BsonDocument>("link_tokens");
        var indexes = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("code"),
                new CreateIndexOptions { Name = "ux_link_tokens_code", Unique = true }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("userId").Ascending("quotaSlot"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ux_link_tokens_active_slot",
                    Unique = true,
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Eq("quotaActive", true)
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("userId")
                    .Ascending("quotaActive")
                    .Descending("expiresAt"),
                new CreateIndexOptions { Name = "ix_link_tokens_user_active_expiry" })
        };
        return collection.Indexes.CreateManyAsync(indexes, cancellationToken);
    }

    private async Task CreateSecurityIndexes(CancellationToken cancellationToken)
    {
        var revokedTokens = context.Database.GetCollection<BsonDocument>("revoked_jwts");
        var resetTokens = context.Database.GetCollection<BsonDocument>("password_reset_tokens");
        await revokedTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { Name = "ttl_revoked_jwts_expiry", ExpireAfter = TimeSpan.Zero }),
            cancellationToken: cancellationToken);
        await resetTokens.Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"),
                new CreateIndexOptions { Name = "ttl_password_reset_tokens_expiry", ExpireAfter = TimeSpan.Zero }),
            cancellationToken: cancellationToken);
    }
}
