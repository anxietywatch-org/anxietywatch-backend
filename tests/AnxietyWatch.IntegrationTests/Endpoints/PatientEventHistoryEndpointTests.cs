using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class PatientEventHistoryEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task UnauthenticatedRequest_ReturnsUnauthorized()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/events")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthenticatedPatientGetsOnlyTheirEmptyHistory()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/events?limit=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PatientEventResponse[]>()).Should().BeEmpty();
    }
}

public sealed record PatientEventResponse(Guid EventId, string Type, DateTimeOffset OccurredAt, string? Status);

public sealed class MongoPatientEventHistoryEndpointTests : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoPatientEventHistoryFactory factory;

    public MongoPatientEventHistoryEndpointTests(MongoDbContainerFixture fixture) =>
        factory = new MongoPatientEventHistoryFactory(fixture.Container.GetConnectionString());

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AuthenticatedPatientWithNoMongoEvents_ReturnsEmptyHistory()
    {
        using var patient = await factory.CreateAuthenticatedClientAsync();

        var response = await patient.GetAsync("/api/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PatientEventResponse[]>()).Should().BeEmpty();
    }

    [Fact]
    public async Task AuthenticatedPatientGetsOnlyOwnMongoEventsWithSafeProjection()
    {
        using var patient = await factory.CreateAuthenticatedClientAsync();
        using var otherPatient = await factory.CreateAuthenticatedClientAsync();
        var patientEventId = Guid.NewGuid();
        var otherEventId = Guid.NewGuid();

        (await patient.PostAsJsonAsync("/api/v1/sos/trigger", Sos(patientEventId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await otherPatient.PostAsJsonAsync("/api/v1/sos/trigger", Sos(otherEventId)))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        var response = await patient.GetAsync("/api/events");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = json.RootElement.EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("eventId").GetGuid().Should().Be(patientEventId);
        item.GetProperty("type").GetString().Should().Be("SOS");
        item.GetProperty("status").GetString().Should().Be("TRIGGERED");
        item.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["eventId", "type", "occurredAt", "status"]);
        json.RootElement.ToString().Should().NotContain(otherEventId.ToString());
    }

    private static object Sos(Guid eventId) => new
    {
        eventId,
        deviceId = Guid.NewGuid(),
        userId = (Guid?)null,
        triggeredAt = DateTimeOffset.UtcNow,
        source = "WATCH",
        reason = "Test alert"
    };
}

internal sealed class MongoPatientEventHistoryFactory(string connectionString) : WebApplicationFactory<Program>
{
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Mongo Event Patient",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "Mongo",
                ["Mongo:ConnectionString"] = connectionString,
                ["Mongo:DatabaseName"] = $"anxietywatch_event_endpoint_tests_{Guid.NewGuid():N}",
                ["Email:VerificationUrl"] = "https://example.test/verify-email",
                ["Email:PasswordResetUrl"] = "https://example.test/reset-password"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, TestEmailSender>();
            services.RemoveAll<IPushNotifier>();
            services.AddSingleton<IPushNotifier, TestPushNotifier>();
        });
    }
}
