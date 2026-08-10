using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;

namespace AnxietyWatch.SecurityTests.Authentication;

public sealed class AuthenticationBaselineTests : IClassFixture<SecurityWebApplicationFactory>
{
    private readonly HttpClient client;

    public AuthenticationBaselineTests(SecurityWebApplicationFactory factory)
    {
        client = factory.CreateClient();
    }

    [Fact]
    public async Task SessionWithoutBearerToken_ShouldReturnUnauthorized()
    {
        var response = await client.GetAsync("/api/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutWithoutBearerToken_ShouldReturnUnauthorized()
    {
        var response = await client.PostAsync("/api/auth/logout", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

public sealed class SecurityWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Testing");
}
