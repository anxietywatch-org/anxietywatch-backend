using System.Net.Http;
using System.Reflection;
using AnxietyWatch.Application.Abstractions.MlInference;
using AnxietyWatch.Infrastructure;
using AnxietyWatch.Infrastructure.MlInference;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MlInferenceDependencyInjectionTests
{
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "AnxietyWatch";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration Config(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(entry => entry.Key, entry => entry.Value))
            .Build();

    private static ServiceProvider BuildProvider(params (string Key, string? Value)[] entries)
    {
        var configuration = Config(entries);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, new TestHostEnvironment());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void MlInferenceClient_IsRegisteredAsSingleImplementation()
    {
        using var provider = BuildProvider(("Persistence:Provider", "InMemory"));

        var client = provider.GetRequiredService<IMlInferenceClient>();

        client.Should().BeOfType<MlInferenceHttpClient>();
    }

    [Fact]
    public void MlInferenceClient_IsRegisteredWithoutMlConfiguration()
    {
        using var provider = BuildProvider(("Persistence:Provider", "InMemory"));

        var client = provider.GetRequiredService<IMlInferenceClient>();

        client.Should().NotBeNull();
    }

    [Fact]
    public void TimeoutSeconds_IsAppliedToHttpClient()
    {
        using var provider = BuildProvider(
            ("Persistence:Provider", "InMemory"),
            ("Ml:Inference:TimeoutSeconds", "7"));

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = factory.CreateClient(typeof(IMlInferenceClient).Name!);

        httpClient.Timeout.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void TimeoutSeconds_DefaultsToTenSeconds()
    {
        using var provider = BuildProvider(("Persistence:Provider", "InMemory"));

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = factory.CreateClient(typeof(IMlInferenceClient).Name!);

        httpClient.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void MlInferenceHttpClient_DisablesAutomaticRedirects()
    {
        using var provider = BuildProvider(("Persistence:Provider", "InMemory"));

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var httpClient = factory.CreateClient(typeof(IMlInferenceClient).Name!);

        var root = GetRootHandler(httpClient);
        root.Should().BeOfType<SocketsHttpHandler>().Which.AllowAutoRedirect.Should().BeFalse();
    }

    private static HttpMessageHandler GetRootHandler(HttpClient httpClient)
    {
        var field = typeof(HttpMessageInvoker).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("HttpMessageInvoker._handler field not found.");
        var handler = (HttpMessageHandler)field.GetValue(httpClient)!;
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler;
        }

        return handler!;
    }
}