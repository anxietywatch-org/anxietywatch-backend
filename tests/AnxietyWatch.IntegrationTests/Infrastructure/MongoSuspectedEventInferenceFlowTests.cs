using AnxietyWatch.Infrastructure.Persistence.Mongo;
using Microsoft.Extensions.Configuration;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoSuspectedEventInferenceFlowTests : SuspectedEventInferenceFlowTests,
    IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context;

    public MongoSuspectedEventInferenceFlowTests(MongoDbContainerFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
                ["Mongo:DatabaseName"] = $"anxietywatch_tests_{Guid.NewGuid():N}"
            })
            .Build();
        context = new MongoContext(configuration);
        SyncRepository = new MongoWearableSyncRepository(context);
        InferenceRepository = new MongoEventInferenceRepository(context);
        BuildService();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() =>
        context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);
}