using AnxietyWatch.Domain.Plans;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.Infrastructure.Persistence.Mongo;

public sealed class MongoPlanRepository(MongoContext context) : IPlanRepository
{
    private IMongoCollection<BsonDocument> Collection =>
        context.Database.GetCollection<BsonDocument>("plans");

    public async Task<IReadOnlyList<Plan>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var documents = await Collection.Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync(cancellationToken);

        return documents.Select(Map).ToArray();
    }

    private static Plan Map(BsonDocument document)
    {
        var type = Enum.Parse<PlanType>(document["id"].AsString, ignoreCase: true);
        return Plan.Create(
            type,
            document["name"].AsString,
            document["priceMonthly"].ToDecimal(),
            document["priceYearly"].ToDecimal(),
            document.GetValue("features", new BsonArray()).AsBsonArray.Select(value => value.AsString),
            document.GetValue("limitations", new BsonArray()).AsBsonArray.Select(value => value.AsString),
            document.GetValue("idealFor", string.Empty).AsString);
    }
}
