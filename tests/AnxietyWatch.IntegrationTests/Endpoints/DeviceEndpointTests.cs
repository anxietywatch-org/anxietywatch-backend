using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

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

        var registerResponse = await client.PostAsJsonAsync("/api/devices/register", new
        {
            platform = "android",
            token = $"fcm-token-{Guid.NewGuid():N}"
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var registered = await registerResponse.Content.ReadFromJsonAsync<DeviceResponse>();
        registered!.Token.Should().NotBeNullOrEmpty();

        var devices = await client.GetFromJsonAsync<DeviceResponse[]>("/api/devices");
        devices.Should().ContainSingle(device => device.Token == registered.Token);

        var unregisterResponse = await client.PostAsJsonAsync("/api/devices/unregister", new
        {
            token = registered.Token
        });
        unregisterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<DeviceResponse[]>("/api/devices")).Should().BeEmpty();
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
    public async Task SosTrigger_ShouldDispatchAlertToLinkedCaregiverDevice()
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

        var deviceRegister = await caregiver.PostAsJsonAsync("/api/devices/register", new
        {
            platform = "android",
            token = $"fcm-{Guid.NewGuid():N}"
        });
        var device = await deviceRegister.Content.ReadFromJsonAsync<DeviceResponse>();

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

        factory.PushNotifier.Messages.Should().ContainSingle();
        factory.PushNotifier.Messages.Single().DeviceTokens.Should().Contain(device!.Token);
    }

    private sealed record AuthResponse(string Token);
    private sealed record DeviceResponse(string Id, string Platform, string Token, DateTimeOffset RegisteredAt);
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