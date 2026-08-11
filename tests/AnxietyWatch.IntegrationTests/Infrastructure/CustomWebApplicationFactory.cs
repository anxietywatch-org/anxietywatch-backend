using System.Collections.Concurrent;
using AnxietyWatch.Application.Abstractions.Security;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Persistence:Provider"] = "InMemory"
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

public sealed class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<TestEmailMessage> messages = new();

    public IReadOnlyCollection<TestEmailMessage> Messages => messages.ToArray();

    public Task SendAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        messages.Enqueue(new TestEmailMessage(recipientEmail, subject, htmlBody));
        return Task.CompletedTask;
    }

    public Task SendHtmlAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default) =>
        SendAsync(recipientEmail, subject, htmlBody, cancellationToken);
}
