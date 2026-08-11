using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class ResendEmailSender(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Email:Resend:ApiKey"];
        var from = configuration["Email:From"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException("Resend email delivery is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from,
                to = new[] { recipientEmail },
                subject,
                html = $"<p>{WebUtility.HtmlEncode(body)}</p>"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Email accepted by Resend for {Recipient}.", recipientEmail);
            return;
        }

        logger.LogError(
            "Resend rejected an email for {Recipient} with status {StatusCode}.",
            recipientEmail,
            (int)response.StatusCode);
        throw new HttpRequestException("Email delivery provider rejected the request.");
    }
}
