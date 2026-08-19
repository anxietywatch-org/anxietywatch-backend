using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.Notifications;

public sealed class PushNotifier(
    ILogger<PushNotifier> logger,
    HttpClient http,
    IConfiguration configuration) : IPushNotifier
{
    private readonly string? _webhookUrl = configuration["Push:WebhookUrl"];

    public async Task NotifyAsync(
        IReadOnlyCollection<string> deviceTokens,
        string title,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            logger.LogInformation(
                "Push notification requested for {DeviceCount} device(s): {Title}",
                deviceTokens.Count,
                title);
            return;
        }

        var payload = new { tokens = deviceTokens, notification = new { title, body } };
        using var response = await http.PostAsJsonAsync(_webhookUrl, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}