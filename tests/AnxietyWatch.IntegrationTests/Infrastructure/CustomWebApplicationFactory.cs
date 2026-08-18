using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public TestEmailSender EmailSender { get; } = new();

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string? email = null)
    {
        var client = CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Integration User",
            email = email ?? $"{Guid.NewGuid():N}@example.test",
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
                ["Email:PasswordResetUrl"] = "https://example.test/reset-password"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }
}

public sealed record TestEmailMessage(string Recipient, string Subject, string HtmlBody);

public sealed record AuthResponse(string Token);

public sealed class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<TestEmailMessage> messages = new();
    private readonly ConcurrentDictionary<string, byte> failingRecipients = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TimeSpan> delayedRecipients = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<TestEmailMessage> Messages => messages.ToArray();

    public void FailNextDelivery(string recipientEmail) => failingRecipients[recipientEmail] = 0;

    public void DelayNextDelivery(string recipientEmail, TimeSpan delay) => delayedRecipients[recipientEmail] = delay;

    public async Task<TestEmailMessage> WaitForMessageAsync(
        string recipientEmail,
        string subject,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var message = messages.FirstOrDefault(candidate =>
                candidate.Recipient == recipientEmail && candidate.Subject == subject);
            if (message is not null) return message;
            await Task.Delay(20);
        }

        throw new TimeoutException("The expected email was not delivered by the test sender.");
    }

    public async Task SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (delayedRecipients.TryRemove(recipientEmail, out var delay))
        {
            await Task.Delay(delay, cancellationToken);
        }

        if (failingRecipients.TryRemove(recipientEmail, out _))
        {
            throw new EmailDeliveryException("Simulated email delivery failure.");
        }

        messages.Enqueue(new TestEmailMessage(recipientEmail, subject, htmlBody));
    }

    public Task SendHtmlAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default) =>
        SendAsync(recipientEmail, subject, htmlBody, cancellationToken);
}
