using Testcontainers.MongoDb;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoDbContainerFixture : IAsyncLifetime
{
    public MongoDbContainer Container { get; } = new MongoDbBuilder()
        .WithImage("mongo:8")
        .Build();

    public Task InitializeAsync() => Container.StartAsync();

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}
