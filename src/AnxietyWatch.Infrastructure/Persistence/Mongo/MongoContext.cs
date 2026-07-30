using Microsoft.Extensions.Configuration;
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
    }

    public IMongoDatabase Database { get; }
}
