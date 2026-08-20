using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.MlInference;
using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Features.Wearables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class InferenceTestFactory : WebApplicationFactory<Program>
{
    public FakeMlInferenceClient MlClient { get; } = new();

    public TestPushNotifier PushNotifier { get; } = new();

    public IEventInferenceRepository Inferences =>
        Services.GetRequiredService<IEventInferenceRepository>();

    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Inference User",
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
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory",
                ["Email:VerificationUrl"] = "https://example.test/verify-email",
                ["Email:PasswordResetUrl"] = "https://example.test/reset-password",
                ["Ml:Inference:TelemetryLookbackSeconds"] = "60"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new TestEmailSender());
            services.RemoveAll<IPushNotifier>();
            services.AddSingleton<IPushNotifier>(PushNotifier);
            services.RemoveAll<IMlInferenceClient>();
            services.AddSingleton<IMlInferenceClient>(MlClient);
        });
    }
}