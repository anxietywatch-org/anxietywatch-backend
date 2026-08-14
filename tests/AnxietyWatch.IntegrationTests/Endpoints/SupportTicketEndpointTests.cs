using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AnxietyWatch.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Endpoints;

public sealed class SupportTicketEndpointTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Tickets_ShouldRequireAuthenticationAndValidateInput()
    {
        using var anonymous = factory.CreateClient();
        var unauthorized = await anonymous.PostAsJsonAsync("/api/support/tickets", ValidRequest());
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var authenticated = await CreateAuthenticatedClient();
        var invalid = await authenticated.PostAsJsonAsync("/api/support/tickets", new
        {
            subject = "x",
            category = "unknown",
            priority = "urgent",
            message = "short"
        });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateThenRead_ShouldPersistTicketForItsOwner()
    {
        using var client = await CreateAuthenticatedClient();
        var create = await client.PostAsJsonAsync("/api/support/tickets", ValidRequest());

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = await create.Content.ReadFromJsonAsync<TicketResponse>();
        ticket.Should().NotBeNull();
        ticket!.Status.Should().Be("open");
        ticket.Category.Should().Be("technical");
        create.Headers.Location.Should().NotBeNull();

        var byId = await client.GetFromJsonAsync<TicketResponse>(create.Headers.Location);
        byId.Should().BeEquivalentTo(ticket);
        var list = await client.GetFromJsonAsync<TicketResponse[]>("/api/support/tickets");
        list.Should().Contain(candidate => candidate.Id == ticket.Id);
    }

    [Fact]
    public async Task TicketFromAnotherUser_ShouldReturnNotFound()
    {
        using var owner = await CreateAuthenticatedClient();
        var create = await owner.PostAsJsonAsync("/api/support/tickets", ValidRequest());
        var ticket = await create.Content.ReadFromJsonAsync<TicketResponse>();

        using var otherUser = await CreateAuthenticatedClient();
        var response = await otherUser.GetAsync($"/api/support/tickets/{ticket!.Id:D}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HttpClient> CreateAuthenticatedClient()
    {
        var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new
        {
            fullName = "Support User",
            email = $"{Guid.NewGuid():N}@example.test",
            password = "Password1",
            planId = "free",
            billingCycle = "monthly",
            paymentMethodToken = (string?)null
        });
        var auth = await registration.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    private static object ValidRequest() => new
    {
        subject = "Problema de sincronización",
        category = "technical",
        priority = "normal",
        message = "El reloj dejó de sincronizar los datos con el teléfono."
    };

    private sealed record AuthResponse(string Token);
    private sealed record TicketResponse(
        Guid Id,
        string Subject,
        string Category,
        string Priority,
        string Message,
        string Status,
        DateTimeOffset CreatedAt);
}
