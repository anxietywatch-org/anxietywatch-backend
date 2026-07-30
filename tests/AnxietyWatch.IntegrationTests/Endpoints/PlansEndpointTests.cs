using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class PlansEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task GetPlans_ShouldReturnTheFourDocumentedPlans()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/plans");

        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Gratuito");
        body.Should().Contain("Individual");
        body.Should().Contain("Familiar");
        body.Should().Contain("Profesional");
    }
}
