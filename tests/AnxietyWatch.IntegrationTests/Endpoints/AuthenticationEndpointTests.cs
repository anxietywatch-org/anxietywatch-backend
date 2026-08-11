using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class AuthenticationEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task RegisterThenSession_ShouldReturnTheAuthenticatedUser()
    {
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Anxiety Watch User",
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });

        registerResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
        var registration = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        registration.Should().NotBeNull();
        registration!.User.Email.Should().Be(email);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration.Token);
        var sessionResponse = await client.GetAsync("/api/auth/session");

        sessionResponse.IsSuccessStatusCode.Should().BeTrue();
        var session = await sessionResponse.Content.ReadFromJsonAsync<AuthResponse>();
        session!.Token.Should().NotBeNullOrWhiteSpace();
        session.User.Email.Should().Be(email);
        session.User.PlanId.Should().Be("free");
    }

    [Fact]
    public async Task LogoutThenSession_ShouldRejectTheRevokedToken()
    {
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Logout Test User",
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var registration = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration!.Token);

        var logoutResponse = await client.PostAsync("/api/auth/logout", null);
        var sessionResponse = await client.GetAsync("/api/auth/session");

        logoutResponse.IsSuccessStatusCode.Should().BeTrue();
        sessionResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FiveFailedLogins_ShouldActivateTheSixtySecondLockout()
    {
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Lockout Test User",
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });

        HttpResponseMessage? fifthAttempt = null;
        for (var index = 0; index < 5; index++)
        {
            fifthAttempt = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email,
                password = "WrongPassword1"
            });
        }

        fifthAttempt!.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        fifthAttempt.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task VerificationEmail_ShouldContainARealLinkAndConfirmOnce()
    {
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Verification Test User",
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var registration = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration!.Token);

        var resendResponse = await client.PostAsync("/api/auth/verify-email/resend", null);

        resendResponse.IsSuccessStatusCode.Should().BeTrue();
        var emailMessage = factory.EmailSender.Messages.Single(message => message.Recipient == email);
        emailMessage.Subject.Should().Be("Verify your AnxietyWatch email");
        emailMessage.HtmlBody.Should().Contain("https://mangoon.xyz/verify-email#token=");
        emailMessage.HtmlBody.Should().Contain("Verify email");
        var token = Regex.Match(emailMessage.HtmlBody, "token=([A-F0-9]{64})").Groups[1].Value;
        token.Should().HaveLength(64);

        using var anonymousClient = factory.CreateClient();
        var confirmation = await anonymousClient.PostAsJsonAsync(
            "/api/auth/verify-email/confirm",
            new { token });
        var replay = await anonymousClient.PostAsJsonAsync(
            "/api/auth/verify-email/confirm",
            new { token });
        var status = await client.GetFromJsonAsync<VerificationStatusResponse>(
            "/api/auth/verify-email/status");

        confirmation.IsSuccessStatusCode.Should().BeTrue();
        replay.StatusCode.Should().Be(System.Net.HttpStatusCode.Gone);
        status!.EmailVerified.Should().BeTrue();
    }

    private sealed record AuthResponse(string Token, UserResponse User);

    private sealed record UserResponse(string Id, string FullName, string Email, string PlanId, bool EmailVerified);
    private sealed record VerificationStatusResponse(bool EmailVerified);
}
