using AnxietyWatch.Infrastructure.Persistence.Mongo;
using Microsoft.Extensions.Configuration;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoTelemetryWindowQueryTests : TelemetryWindowQueryTests, IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context;

    public MongoTelemetryWindowQueryTests(MongoDbContainerFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
                ["Mongo:DatabaseName"] = $"anxietywatch_tests_{Guid.NewGuid():N}"
            })
            .Build();
        context = new MongoContext(configuration);
        Repository = new MongoWearableSyncRepository(context);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() =>
        context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);
}