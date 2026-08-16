using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
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
        CancellationToken cancellationToken = default) =>
        await SendHtmlAsync(
            recipientEmail,
            subject,
            $"<p>{WebUtility.HtmlEncode(body)}</p>",
            cancellationToken);

    public async Task SendHtmlAsync(
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var apiKey = configuration["Email:Resend:ApiKey"];
        var from = configuration["Email:From"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from))
        {
            throw new EmailDeliveryException("Email delivery is not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "emails")
        {
            Content = JsonContent.Create(new
            {
                from,
                to = new[] { recipientEmail },
                subject,
                html = htmlBody
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Resend email delivery timed out.");
            throw new EmailDeliveryException("Email delivery timed out.", true, exception);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Resend email delivery failed.");
            throw new EmailDeliveryException(
                "Email delivery failed.",
                DeliveryMayHaveSucceeded(exception),
                exception);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Email accepted by Resend.");
                return;
            }

            logger.LogError(
                "Resend rejected an email with status {StatusCode}.",
                (int)response.StatusCode);
            throw new EmailDeliveryException(
                "Email delivery provider rejected the request.",
                (int)response.StatusCode >= 500);
        }
    }

    private static bool DeliveryMayHaveSucceeded(HttpRequestException exception) =>
        exception.HttpRequestError is not (
            HttpRequestError.NameResolutionError or
            HttpRequestError.ConnectionError or
            HttpRequestError.SecureConnectionError or
            HttpRequestError.ProxyTunnelError);
}
