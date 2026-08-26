using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoContext
{
    public MongoContext(IConfiguration configuration)
    {
        var connectionString = configuration["Mongo:ConnectionString"]
            ?? throw new InvalidOperationException("Mongo:ConnectionString is not configured.");
        var databaseName = configuration["Mongo:DatabaseName"]
            ?? throw new InvalidOperationException("Mongo:DatabaseName is not configured.");

        Database = new MongoClient(connectionString).GetDatabase(databaseName);
        EnsureIndexes();
    }

    public IMongoDatabase Database { get; }

    private void EnsureIndexes()
    {
        CreateIndex("plans", Builders<BsonDocument>.IndexKeys.Ascending("id"), unique: true);
        CreateIndex("users", Builders<BsonDocument>.IndexKeys.Ascending("email"), unique: true);
        var verificationTokenIndex = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("emailVerificationTokenHash"),
            new CreateIndexOptions { Name = "ux_users_email_verification_token", Unique = true, Sparse = true });
        Database.GetCollection<BsonDocument>("users").Indexes.CreateOne(verificationTokenIndex);
        CreateIndex("episodes", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("date"));
        CreateIndex("link_tokens", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("expiresAt"));
        CreateIndex("link_tokens", Builders<BsonDocument>.IndexKeys
            .Ascending("userId")
            .Ascending("acceptedBy")
            .Ascending("status")
            .Ascending("role"));
        CreateIndex("link_tokens", Builders<BsonDocument>.IndexKeys
            .Ascending("acceptedBy")
            .Ascending("status")
            .Ascending("role")
            .Ascending("acceptedAt"));
        CreateIndex("link_tokens", Builders<BsonDocument>.IndexKeys.Ascending("code"), unique: true);
        var tokenQuotaIndex = new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("userId").Ascending("quotaSlot"),
            new CreateIndexOptions<BsonDocument>
            {
                Name = "ux_link_tokens_active_slot",
                Unique = true,
                PartialFilterExpression = Builders<BsonDocument>.Filter.Eq("quotaActive", true)
            });
        Database.GetCollection<BsonDocument>("link_tokens").Indexes.CreateOne(tokenQuotaIndex);
        CreateIndex("revoked_tokens", Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"), expiresAfter: TimeSpan.Zero);
        CreateIndex("password_reset_tokens", Builders<BsonDocument>.IndexKeys.Ascending("expiresAt"), expiresAfter: TimeSpan.Zero);
        CreateIndex("support_tickets", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("createdAt"));
        CreateIndex("billing_transactions", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("createdAt"));
        CreateIndex("device_tokens", Builders<BsonDocument>.IndexKeys.Ascending("token"), unique: true);
        CreateIndex("device_tokens", Builders<BsonDocument>.IndexKeys.Ascending("userId"));
        CreateIndex("notification_outbox", Builders<BsonDocument>.IndexKeys.Ascending("dedupeKey"), unique: true);
        CreateIndex("notification_outbox", Builders<BsonDocument>.IndexKeys
            .Ascending("status").Ascending("nextAttemptAt").Ascending("leaseUntil"));
        CreateIndex("telemetry_batches", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("Samples.Timestamp"));
        CreateIndex("sos_events", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("TriggeredAt"));
        CreateIndex("sos_cancellations", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("CancelledAt"));
        CreateIndex("suspected_events", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("DetectedAt"));
        CreateIndex("event_decisions", Builders<BsonDocument>.IndexKeys.Ascending("userId").Descending("RespondedAt"));
    }

    private void CreateIndex(
        string collectionName,
        IndexKeysDefinition<BsonDocument> keys,
        bool unique = false,
        TimeSpan? expiresAfter = null)
    {
        var options = new CreateIndexOptions { Unique = unique, ExpireAfter = expiresAfter };
        Database.GetCollection<BsonDocument>(collectionName).Indexes.CreateOne(
            new CreateIndexModel<BsonDocument>(keys, options));
    }
}
