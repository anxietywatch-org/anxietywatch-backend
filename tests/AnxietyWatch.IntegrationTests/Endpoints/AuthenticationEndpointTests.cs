using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class AuthenticationEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Register_ShouldRejectPaidPlanWithoutCheckout()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Paid Plan Bypass",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "professional",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

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
        var verificationEmail = await factory.EmailSender.WaitForMessageAsync(
            email,
            "Verify your AnxietyWatch email",
            TimeSpan.FromSeconds(2));
        verificationEmail.HtmlBody.Should().Contain("Verifica tu correo");
        verificationEmail.HtmlBody.Should().Contain("https://example.test/verify-email#token=");

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration.Token);
        var sessionResponse = await client.GetAsync("/api/auth/session");

        sessionResponse.IsSuccessStatusCode.Should().BeTrue();
        var session = await sessionResponse.Content.ReadFromJsonAsync<AuthResponse>();
        session!.Token.Should().NotBeNullOrWhiteSpace();
        session.User.Email.Should().Be(email);
        session.User.PlanId.Should().Be("free");
        session.User.Role.Should().Be("patient");
        registration!.User.Role.Should().Be("patient");
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
    public async Task ForgotPassword_ShouldNotEnumerateUsersWhenEmailDeliveryFails()
    {
        using var client = factory.CreateClient();
        var knownEmail = $"{Guid.NewGuid():N}@example.test";
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Forgot Password User",
            email = knownEmail,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });

        factory.EmailSender.DelayNextDelivery(knownEmail, TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();
        var accepted = await client.PostAsJsonAsync(
            "/api/auth/password/forgot",
            new { email = knownEmail });
        stopwatch.Stop();
        await factory.EmailSender.WaitForMessageAsync(
            knownEmail,
            "AnxietyWatch password recovery",
            TimeSpan.FromSeconds(5));
        var failedEmail = $"{Guid.NewGuid():N}@example.test";
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Failed Forgot Password User",
            email = failedEmail,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        factory.EmailSender.FailNextDelivery(failedEmail);
        var failedDelivery = await client.PostAsJsonAsync(
            "/api/auth/password/forgot",
            new { email = failedEmail });
        var unknown = await client.PostAsJsonAsync(
            "/api/auth/password/forgot",
            new { email = $"{Guid.NewGuid():N}@example.test" });
        var secondKnownEmail = $"{Guid.NewGuid():N}@example.test";
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Second Forgot Password User",
            email = secondKnownEmail,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var afterFailure = await client.PostAsJsonAsync(
            "/api/auth/password/forgot",
            new { email = secondKnownEmail });
        await factory.EmailSender.WaitForMessageAsync(
            secondKnownEmail,
            "AnxietyWatch password recovery",
            TimeSpan.FromSeconds(5));

        accepted.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        failedDelivery.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        unknown.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        afterFailure.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var acceptedBody = await accepted.Content.ReadFromJsonAsync<MessageResponse>();
        var failedBody = await failedDelivery.Content.ReadFromJsonAsync<MessageResponse>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<MessageResponse>();
        failedBody.Should().BeEquivalentTo(acceptedBody);
        unknownBody.Should().BeEquivalentTo(acceptedBody);
    }

    [Fact]
    public async Task VerificationResend_ShouldRollbackAndReturnServiceUnavailableWhenDeliveryFails()
    {
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        factory.EmailSender.FailNextDelivery(email);
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Resend Failure User",
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var registration = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration!.Token);

        factory.EmailSender.FailNextDelivery(email);
        var failedDelivery = await client.PostAsync("/api/auth/verify-email/resend", null);
        var retry = await client.PostAsync("/api/auth/verify-email/resend", null);
        var cooldown = await client.PostAsync("/api/auth/verify-email/resend", null);

        failedDelivery.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
        (await failedDelivery.Content.ReadFromJsonAsync<ProblemResponse>())!.Title
            .Should().Be("Email delivery is temporarily unavailable.");
        failedDelivery.Headers.RetryAfter.Should().NotBeNull();
        retry.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        cooldown.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        cooldown.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task ChangePassword_ShouldReturnSuccessWhenNotificationFails()
    {
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Password Change User",
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var registration = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration!.Token);
        factory.EmailSender.FailNextDelivery(email);

        var change = await client.PostAsJsonAsync("/api/auth/change-password", new
        {
            currentPassword = "Password1",
            newPassword = "NewPassword2"
        });
        var oldSession = await client.GetAsync("/api/auth/session");
        using var anonymousClient = factory.CreateClient();
        var oldLogin = await anonymousClient.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "Password1"
        });
        var newLogin = await anonymousClient.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "NewPassword2"
        });

        change.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        oldSession.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        oldLogin.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        newLogin.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_ShouldInvalidateExistingSessions()
    {
        using var client = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@example.test";
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Password Reset User",
            email,
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var registration = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration!.Token);
        await client.PostAsJsonAsync("/api/auth/password/forgot", new { email });
        var resetToken = (await factory.EmailSender.WaitForMessageAsync(
            email,
            "AnxietyWatch password recovery",
            TimeSpan.FromSeconds(5))).HtmlBody;
        resetToken = Regex.Match(resetToken, "token=([A-F0-9]{64})").Groups[1].Value;
        resetToken.Should().HaveLength(64);

        using var anonymousClient = factory.CreateClient();
        var reset = await anonymousClient.PostAsJsonAsync("/api/auth/password/reset", new
        {
            token = resetToken,
            newPassword = "ResetPassword2"
        });
        var oldSession = await client.GetAsync("/api/auth/session");
        var newLogin = await anonymousClient.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = "ResetPassword2"
        });

        reset.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        oldSession.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        newLogin.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
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

        var emailMessage = factory.EmailSender.Messages.Single(message => message.Recipient == email);
        emailMessage.Subject.Should().Be("Verify your AnxietyWatch email");
        emailMessage.HtmlBody.Should().Contain("https://example.test/verify-email#token=");
        emailMessage.HtmlBody.Should().Contain("Verificar mi correo");
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

    private sealed record UserResponse(
        string Id,
        string FullName,
        string Email,
        string PlanId,
        bool EmailVerified,
        string? AvatarUrl = null,
        string Role = "patient");
    private sealed record VerificationStatusResponse(bool EmailVerified);
    private sealed record MessageResponse(string Message);
    private sealed record ProblemResponse(string Title, int Status);
}
