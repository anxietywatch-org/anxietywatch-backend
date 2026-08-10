using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class ProfileSettingsEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task GetThenUpdateProfileAndSettings_ShouldReturnPersistedValues()
    {
        using var client = await CreateAuthenticatedClient();

        var initialProfile = await client.GetFromJsonAsync<ProfileResponse>("/api/profile");
        var initialSettings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        initialProfile.Should().Be(new ProfileResponse("Profile Settings User", null));
        initialSettings.Should().Be(new SettingsResponse(70, true, false));

        var profileResponse = await client.PatchAsJsonAsync("/api/profile", new
        {
            fullName = "Updated User",
            avatarUrl = "https://example.test/avatar.png"
        });
        var settingsResponse = await client.PatchAsJsonAsync("/api/settings", new
        {
            anxietyThreshold = 55,
            pushNotifications = false,
            privateMode = false
        });

        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        settingsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<ProfileResponse>("/api/profile"))
            .Should().Be(new ProfileResponse("Updated User", "https://example.test/avatar.png"));
        (await client.GetFromJsonAsync<SettingsResponse>("/api/settings"))
            .Should().Be(new SettingsResponse(55, false, false));
    }

    [Fact]
    public async Task GetProfileAndSettings_WithoutAuthentication_ShouldReturnUnauthorized()
    {
        using var client = factory.CreateClient();

        (await client.GetAsync("/api/profile")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Profile Settings User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var registration = await response.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", registration!.Token);
        return client;
    }

    private sealed record AuthResponse(string Token);
    private sealed record ProfileResponse(string FullName, string? AvatarUrl);
    private sealed record SettingsResponse(int AnxietyThreshold, bool PushNotifications, bool PrivateMode);
}
