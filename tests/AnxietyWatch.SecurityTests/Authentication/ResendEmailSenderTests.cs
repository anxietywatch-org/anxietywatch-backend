using System.Net;
using System.Text.Json;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AnxietyWatch.SecurityTests.Authentication;

public sealed class ResendEmailSenderTests
{
    [Fact]
    public async Task SendAsync_ShouldEscapeTextAndAcceptSuccessfulDelivery()
    {
        string? payload = null;
        var sender = CreateSender(async request =>
        {
            payload = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        await sender.SendAsync("user@example.test", "Subject", "<unsafe>");

        using var document = JsonDocument.Parse(payload!);
        var html = document.RootElement.GetProperty("html").GetString();
        html.Should().Contain("&lt;unsafe&gt;");
        html.Should().NotContain("<unsafe>");
    }

    [Fact]
    public async Task SendAsync_ShouldTranslateProviderRejection()
    {
        var sender = CreateSender(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)));

        var exception = await FluentActions.Invoking(() =>
                sender.SendAsync("user@example.test", "Subject", "Body"))
            .Should().ThrowAsync<EmailDeliveryException>();
        exception.Which.DeliveryMayHaveSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ShouldTranslateNetworkFailure()
    {
        var sender = CreateSender(_ =>
            throw new HttpRequestException("Network unavailable."));

        var exception = await FluentActions.Invoking(() =>
                sender.SendAsync("user@example.test", "Subject", "Body"))
            .Should().ThrowAsync<EmailDeliveryException>();
        exception.Which.DeliveryMayHaveSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ShouldTreatProviderServerErrorsAsAmbiguous()
    {
        var sender = CreateSender(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var exception = await FluentActions.Invoking(() =>
                sender.SendAsync("user@example.test", "Subject", "Body"))
            .Should().ThrowAsync<EmailDeliveryException>();
        exception.Which.DeliveryMayHaveSucceeded.Should().BeTrue();
    }

    private static ResendEmailSender CreateSender(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Resend:ApiKey"] = "re_test",
                ["Email:From"] = "AnxietyWatch <no-reply@mail.mangoon.xyz>"
            })
            .Build();
        var client = new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://api.resend.com/")
        };
        return new ResendEmailSender(
            client,
            configuration,
            NullLogger<ResendEmailSender>.Instance);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request);
    }
}
