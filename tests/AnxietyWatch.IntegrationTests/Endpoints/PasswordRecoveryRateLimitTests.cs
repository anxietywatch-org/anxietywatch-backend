using System.Net;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class PasswordRecoveryRateLimitTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task ForgotPassword_ShouldRateLimitRepeatedAnonymousRequests()
    {
        using var client = factory.CreateClient();
        HttpResponseMessage? response = null;
        for (var index = 0; index < 21; index++)
        {
            response = await client.PostAsJsonAsync(
                "/api/auth/password/forgot",
                new { email = $"unknown-{index}@example.test" });
        }

        response!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task ForgotPassword_ShouldPartitionForwardedClientAddresses()
    {
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        firstClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        secondClient.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.11");
        for (var index = 0; index < 20; index++)
        {
            (await firstClient.PostAsJsonAsync(
                "/api/auth/password/forgot",
                new { email = $"first-{index}@example.test" })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var blocked = await firstClient.PostAsJsonAsync(
            "/api/auth/password/forgot",
            new { email = "first-blocked@example.test" });
        var otherAddress = await secondClient.PostAsJsonAsync(
            "/api/auth/password/forgot",
            new { email = "second-allowed@example.test" });

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        otherAddress.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
