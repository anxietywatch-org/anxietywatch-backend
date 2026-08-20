using AnxietyWatch.Infrastructure.Persistence;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class InMemoryTelemetryWindowQueryTests : TelemetryWindowQueryTests, IAsyncLifetime
{
    public InMemoryTelemetryWindowQueryTests() => Repository = new InMemoryWearableSyncRepository();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;
}