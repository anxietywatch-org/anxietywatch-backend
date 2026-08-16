using AnxietyWatch.Domain.Billing;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoBillingTransactionRepository(MongoContext context) : IBillingTransactionRepository
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("billing_transactions");

    public Task AddAsync(BillingTransaction transaction, CancellationToken cancellationToken = default) =>
        Collection.InsertOneAsync(new BsonDocument
        {
            ["_id"] = transaction.Id.ToString(),
            ["userId"] = transaction.UserId.ToString(),
            ["planId"] = transaction.PlanId,
            ["billingCycle"] = transaction.BillingCycle,
            ["amount"] = transaction.Amount,
            ["currency"] = transaction.Currency,
            ["createdAt"] = transaction.CreatedAt.UtcDateTime,
            ["status"] = transaction.Status,
            ["simulated"] = transaction.Simulated
        }, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<BillingTransaction>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var documents = await Collection.Find(Builders<BsonDocument>.Filter.Eq("userId", userId.ToString()))
            .SortByDescending(document => document["createdAt"])
            .ToListAsync(cancellationToken);
        return documents.Select(document => new BillingTransaction(
            Guid.Parse(document["_id"].AsString),
            userId,
            document["planId"].AsString,
            document["billingCycle"].AsString,
            document["amount"].ToDecimal(),
            document["currency"].AsString,
            new DateTimeOffset(document["createdAt"].ToUniversalTime(), TimeSpan.Zero),
            document["status"].AsString,
            document.GetValue("simulated", true).ToBoolean())).ToArray();
    }
}
