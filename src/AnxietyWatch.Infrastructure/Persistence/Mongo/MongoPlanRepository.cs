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

        if (documents.Count == 0)
        {
            await SeedDefaultPlansAsync(cancellationToken);
            documents = await Collection.Find(FilterDefinition<BsonDocument>.Empty)
                .ToListAsync(cancellationToken);
        }

        return documents.Select(Map).ToArray();
    }

    private async Task SeedDefaultPlansAsync(CancellationToken cancellationToken)
    {
        foreach (var plan in DefaultPlans)
        {
            await Collection.ReplaceOneAsync(
                Builders<BsonDocument>.Filter.Eq("id", plan["id"]),
                plan,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);
        }
    }

    private static readonly IReadOnlyList<BsonDocument> DefaultPlans =
    [
        CreatePlan("free", "Gratuito", 0, 0,
            ["Dashboard", "Registro de ansiedad"],
            ["1 token", "5 registros semanales"],
            "Usuarios que desean probar AnxietyWatch"),
        CreatePlan("individual", "Individual", 9.99m, 95.90m,
            ["Dashboard", "Historial ampliado", "Modo privado"],
            ["1 token"],
            "Uso personal"),
        CreatePlan("family", "Familiar", 14.99m, 143.90m,
            ["Dashboard familiar", "Miembros vinculados"],
            ["5 tokens"],
            "Familias"),
        CreatePlan("professional", "Profesional", 29.99m, 287.90m,
            ["Reportes clínicos", "Dashboard de pacientes"],
            ["20 tokens"],
            "Profesionales de la salud")
    ];

    private static BsonDocument CreatePlan(
        string id,
        string name,
        decimal priceMonthly,
        decimal priceYearly,
        IEnumerable<string> features,
        IEnumerable<string> limitations,
        string idealFor) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["priceMonthly"] = new BsonDecimal128(priceMonthly),
        ["priceYearly"] = new BsonDecimal128(priceYearly),
        ["features"] = new BsonArray(features),
        ["limitations"] = new BsonArray(limitations),
        ["idealFor"] = idealFor
    };

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
