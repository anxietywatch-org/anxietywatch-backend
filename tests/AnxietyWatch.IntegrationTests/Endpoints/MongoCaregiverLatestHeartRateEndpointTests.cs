using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class MongoCaregiverLatestHeartRateEndpointTests(
    MongoDbContainerFixture fixture) : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoCaregiverTelemetryFactory factory =
        new(fixture.Container.GetConnectionString(), $"aw_cg_hr_{Guid.NewGuid():N}");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await factory.MongoClient.DropDatabaseAsync(factory.DatabaseName);
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task EmptyMongoTelemetry_ReturnsNoContentFromBothRoutes()
    {
        var patient = await CreateUserAsync("Patient");
        var caregiver = await CreateUserAsync("Caregiver");
        await AddAcceptedRelationshipAsync(patient.UserId, caregiver.UserId);

        (await caregiver.Client.GetAsync($"/api/caregiver/patients/{patient.UserId}/telemetry/latest"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await caregiver.Client.GetAsync($"/api/caregiver/patients/{patient.UserId}/heart-rate/latest"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateUserAsync(string name)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = name,
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return (client, Guid.Parse(auth.User.Id));
    }

    private async Task AddAcceptedRelationshipAsync(Guid patientId, Guid caregiverId)
    {
        var token = new LinkToken(Guid.NewGuid(), patientId, $"AW-{Guid.NewGuid():N}"[..15], "family_member", DateTimeOffset.UtcNow.AddDays(30));
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ILinkTokenRepository>();
        (await tokens.TryAddAsync(token, 10)).Should().BeTrue();
        (await tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, DateTimeOffset.UtcNow)).Should().BeTrue();
    }

    private sealed record AuthResponse(string Token, UserResponse User);
    private sealed record UserResponse(string Id);
}

internal sealed class MongoCaregiverTelemetryFactory : WebApplicationFactory<Program>
{
    private readonly string connectionString;
    public string DatabaseName { get; }
    public MongoClient MongoClient { get; }

    public MongoCaregiverTelemetryFactory(string connectionString, string databaseName)
    {
        this.connectionString = connectionString;
        DatabaseName = databaseName;
        MongoClient = new MongoClient(connectionString);
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
                ["Mongo:DatabaseName"] = DatabaseName,
                ["Email:VerificationUrl"] = "https://example.test/verify-email",
                ["Email:PasswordResetUrl"] = "https://example.test/reset-password"
            }));
    }
}
