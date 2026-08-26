using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AnxietyWatch.Domain.Devices;
using AnxietyWatch.Domain.Notifications;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class DeviceEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task RegisterListUnregisterDevice_ShouldRoundTrip()
    {
        using var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Device User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        var token = $"fcm-token-{Guid.NewGuid():N}";
        var registerResponse = await client.PostAsJsonAsync("/api/devices/register", new
        {
            platform = "android",
            token
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DeviceResponse>();
        registered!.Platform.Should().Be("android");
        (await registerResponse.Content.ReadAsStringAsync()).Should().NotContain(token);

        var devices = await client.GetFromJsonAsync<DeviceResponse[]>("/api/devices");
        devices.Should().ContainSingle(device => device.Id == registered.Id);

        var unregisterResponse = await client.PostAsJsonAsync("/api/devices/unregister", new
        {
            token
        });
        unregisterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<DeviceResponse[]>("/api/devices")).Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterDevice_IsIdempotentUpdatesPlatformAndSupportsMultipleTokens()
    {
        var (client, userId) = await CreateAuthenticatedClientAsync();
        var firstToken = $"fcm-{Guid.NewGuid():N}";
        var secondToken = $"fcm-{Guid.NewGuid():N}";

        var first = await RegisterAsync(client, "android", firstToken);
        var repeated = await RegisterAsync(client, "ios", firstToken);
        await RegisterAsync(client, "android", secondToken);

        repeated.Id.Should().Be(first.Id);
        repeated.Platform.Should().Be("ios");
        repeated.RegisteredAt.Should().Be(first.RegisteredAt);
        repeated.UpdatedAt.Should().BeOnOrAfter(first.UpdatedAt);
        var persisted = await GetDevicesForUserAsync(userId);
        persisted.Should().HaveCount(2);
        persisted.Should().Contain(device => device.Token == firstToken && device.Platform == "ios");
        persisted.Should().Contain(device => device.Token == secondToken);
    }

    [Fact]
    public async Task RegisterDevice_TransfersTokenOwnershipAndBodyCannotAssignAnotherUser()
    {
        var (first, firstId) = await CreateAuthenticatedClientAsync();
        var (second, secondId) = await CreateAuthenticatedClientAsync();
        var token = $"fcm-{Guid.NewGuid():N}";
        await first.PostAsJsonAsync("/api/devices/register", new
        {
            platform = "android",
            token,
            userId = secondId
        });

        (await GetDevicesForUserAsync(firstId)).Should().ContainSingle(device => device.Token == token);
        (await GetDevicesForUserAsync(secondId)).Should().BeEmpty();

        (await RegisterAsync(second, "ios", token)).Platform.Should().Be("ios");

        (await GetDevicesForUserAsync(firstId)).Should().BeEmpty();
        (await GetDevicesForUserAsync(secondId)).Should().ContainSingle(device => device.Token == token);
    }

    [Theory]
    [InlineData("", "token")]
    [InlineData("windows", "token")]
    [InlineData("android", "")]
    public async Task RegisterDevice_RejectsInvalidPlatformOrToken(string platform, string token)
    {
        var (client, _) = await CreateAuthenticatedClientAsync();

        (await client.PostAsJsonAsync("/api/devices/register", new { platform, token }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PublicDeviceResponsesNeverExposeFcmToken()
    {
        var (client, _) = await CreateAuthenticatedClientAsync();
        var token = $"fcm-secret-{Guid.NewGuid():N}";

        var register = await client.PostAsJsonAsync("/api/devices/register", new { platform = "android", token });
        var list = await client.GetAsync("/api/devices");

        (await register.Content.ReadAsStringAsync()).Should().NotContain(token);
        (await list.Content.ReadAsStringAsync()).Should().NotContain(token);
        using var registerJson = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        registerJson.RootElement.TryGetProperty("token", out _).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterDevice_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        using var client = factory.CreateClient();

        (await client.PostAsJsonAsync("/api/devices/register", new
        {
            platform = "android",
            token = "fcm-token"
        })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SosTrigger_ShouldQueueAlertForLinkedCaregiverDevice()
    {
        using var owner = factory.CreateClient();
        var registration = await owner.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "SOS Owner",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var ownerAuth = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerAuth!.Token);

        var tokenResponse = await owner.PostAsJsonAsync("/api/tokens", new { role = "family_member" });
        var created = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();

        using var caregiver = factory.CreateClient();
        var redeem = await caregiver.PostAsJsonAsync("/api/tokens/accept-by-code", new
        {
            code = created!.Code,
            deviceId = "device-1"
        });
        var redeemed = await redeem.Content.ReadFromJsonAsync<TokenRedeemResponse>();
        caregiver.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", redeemed!.Token);

        var caregiverFcmToken = $"fcm-{Guid.NewGuid():N}";
        var deviceRegister = await caregiver.PostAsJsonAsync("/api/devices/register", new
        {
            platform = "android",
            token = caregiverFcmToken
        });
        deviceRegister.StatusCode.Should().Be(HttpStatusCode.OK);

        var sos = await owner.PostAsJsonAsync("/api/v1/sos/trigger", new
        {
            eventId = Guid.NewGuid(),
            deviceId = Guid.NewGuid(),
            userId = (Guid?)null,
            triggeredAt = DateTimeOffset.UtcNow,
            source = "WATCH",
            reason = "Test caregiver alert"
        });
        sos.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = factory.Services.CreateScope();
        var submitted = await sos.Content.ReadFromJsonAsync<SubmissionResponse>();
        var jobs = await scope.ServiceProvider.GetRequiredService<INotificationOutboxRepository>().GetAllAsync();
        jobs.Should().ContainSingle(job => job.EventId == submitted!.EventId);
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Device User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await registration.Content.ReadFromJsonAsync<AuthResponseWithUser>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return (client, Guid.Parse(auth.User.Id));
    }

    private static async Task<DeviceResponse> RegisterAsync(HttpClient client, string platform, string token)
    {
        var response = await client.PostAsJsonAsync("/api/devices/register", new { platform, token });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<DeviceResponse>())!;
    }

    private async Task<IReadOnlyList<DeviceToken>> GetDevicesForUserAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDeviceTokenRepository>().GetForUserAsync(userId);
    }

    private sealed record AuthResponse(string Token);
    private sealed record SubmissionResponse(Guid EventId);
    private sealed record AuthResponseWithUser(string Token, UserResponse User);
    private sealed record DeviceResponse(
        string Id,
        string Platform,
        DateTimeOffset RegisteredAt,
        DateTimeOffset UpdatedAt);
    private sealed record TokenResponse(string Id, string Code, string Role);
    private sealed record TokenRedeemResponse(string Token, DateTimeOffset ExpiresAt, string Role, UserResponse User);
    private sealed record UserResponse(
        string Id,
        string FullName,
        string Email,
        string PlanId,
        bool EmailVerified,
        string? AvatarUrl = null,
        string Role = "patient");
}
