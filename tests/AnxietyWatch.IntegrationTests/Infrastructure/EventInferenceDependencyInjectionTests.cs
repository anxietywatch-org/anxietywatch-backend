using AnxietyWatch.Application.Abstractions.MlInference;
using AnxietyWatch.Application.Features.Wearables;
using AnxietyWatch.Infrastructure;
using AnxietyWatch.Infrastructure.Persistence;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using AnxietyWatch.Infrastructure.Wearables;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class EventInferenceDependencyInjectionTests : IClassFixture<MongoDbContainerFixture>
{
    private readonly MongoDbContainerFixture mongoFixture;

    public EventInferenceDependencyInjectionTests(MongoDbContainerFixture mongoFixture) =>
        this.mongoFixture = mongoFixture;

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "AnxietyWatch";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider BuildProvider(string provider, string? connectionString = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = provider,
                ["Mongo:ConnectionString"] = connectionString,
                ["Mongo:DatabaseName"] = connectionString is null ? null : $"anxietywatch_tests_{Guid.NewGuid():N}"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, new TestHostEnvironment());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void InMemoryInferenceRepository_IsRegistered()
    {
        using var provider = BuildProvider("InMemory");

        var repository = provider.GetRequiredService<IEventInferenceRepository>();

        repository.Should().BeOfType<InMemoryEventInferenceRepository>();
    }

    [Fact]
    public void InMemory_SuspectedEventInferenceService_IsRegistered()
    {
        using var provider = BuildProvider("InMemory");

        var service = provider.GetRequiredService<ISuspectedEventInferenceService>();

        service.Should().BeOfType<SuspectedEventInferenceService>();
    }

    [Fact]
    public void MongoInferenceRepository_IsRegistered()
    {
        using var provider = BuildProvider("Mongo", mongoFixture.Container.GetConnectionString());

        var repository = provider.GetRequiredService<IEventInferenceRepository>();

        repository.Should().BeOfType<MongoEventInferenceRepository>();
    }
}