using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class MongoCaregiverRelationshipAuthorizationTests : IClassFixture<MongoDbContainerFixture>, IAsyncLifetime
{
    private readonly MongoContext context;
    private readonly MongoLinkTokenRepository tokens;

    public MongoCaregiverRelationshipAuthorizationTests(MongoDbContainerFixture fixture)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mongo:ConnectionString"] = fixture.Container.GetConnectionString(),
                ["Mongo:DatabaseName"] = $"anxietywatch_tests_{Guid.NewGuid():N}"
            })
            .Build();
        context = new MongoContext(configuration);
        tokens = new MongoLinkTokenRepository(context);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => context.Database.Client.DropDatabaseAsync(context.Database.DatabaseNamespace.DatabaseName);

    [Fact]
    public async Task AcceptedFamilyMemberLink_AllowsCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        await AddAcceptedAsync(patientId, caregiverId);

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticatedUserGuessesPatientId_DoesNotGrantCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        await AddAcceptedAsync(patientId, Guid.NewGuid());

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, Guid.NewGuid())).Should().BeFalse();
    }

    [Theory]
    [InlineData(TokenStatus.Pending)]
    [InlineData(TokenStatus.Deleted)]
    [InlineData(TokenStatus.Expired)]
    public async Task InactiveTokenStatus_DoesNotGrantCaregiverAccess(TokenStatus status)
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var token = LinkToken.Restore(
            Guid.NewGuid(),
            patientId,
            Code(),
            "family_member",
            DateTimeOffset.UtcNow.AddDays(30),
            status,
            caregiverId,
            DateTimeOffset.UtcNow);
        await tokens.TryAddAsync(token, 10);

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeFalse();
    }

    [Fact]
    public async Task AcceptedTokenWithNullAcceptedBy_DoesNotGrantCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        var token = LinkToken.Restore(
            Guid.NewGuid(),
            patientId,
            Code(),
            "family_member",
            DateTimeOffset.UtcNow.AddDays(30),
            TokenStatus.Accepted,
            null,
            DateTimeOffset.UtcNow);
        await tokens.TryAddAsync(token, 10);

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task DifferentCaregiver_DoesNotGrantCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        await AddAcceptedAsync(patientId, Guid.NewGuid());

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task RevokedRelationship_DoesNotGrantCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var token = await AddAcceptedAsync(patientId, caregiverId);

        (await tokens.TryRevokeAsync(token.Id)).Should().BeTrue();

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeFalse();
    }

    [Fact]
    public async Task SelfRole_DoesNotCreateCrossUserCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        await AddAcceptedAsync(patientId, caregiverId, "self");

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeFalse();
    }

    [Fact]
    public async Task MultiplePatients_OnlyLinkedPatientIsAllowed()
    {
        var caregiverId = Guid.NewGuid();
        var linkedPatientId = Guid.NewGuid();
        var otherPatientId = Guid.NewGuid();
        await AddAcceptedAsync(linkedPatientId, caregiverId);

        (await tokens.HasAcceptedCaregiverRelationshipAsync(linkedPatientId, caregiverId)).Should().BeTrue();
        (await tokens.HasAcceptedCaregiverRelationshipAsync(otherPatientId, caregiverId)).Should().BeFalse();
    }

    [Fact]
    public async Task StartupCreatesCaregiverRelationshipLookupIndex()
    {
        var indexes = await context.Database.GetCollection<BsonDocument>("link_tokens")
            .Indexes
            .ListAsync();
        var documents = await indexes.ToListAsync();

        documents.Should().Contain(document =>
            document["key"].AsBsonDocument.Contains("userId") &&
            document["key"].AsBsonDocument.Contains("acceptedBy") &&
            document["key"].AsBsonDocument.Contains("status") &&
            document["key"].AsBsonDocument.Contains("role"));
    }

    private async Task<LinkToken> AddAcceptedAsync(Guid patientId, Guid caregiverId, string role = "family_member")
    {
        var token = NewToken(patientId, role);
        (await tokens.TryAddAsync(token, 10)).Should().BeTrue();
        (await tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, DateTimeOffset.UtcNow)).Should().BeTrue();
        return token;
    }

    private static LinkToken NewToken(Guid patientId, string role = "family_member") =>
        new(Guid.NewGuid(), patientId, Code(), role, DateTimeOffset.UtcNow.AddDays(30));

    private static string Code() => $"AW-{Guid.NewGuid():N}"[..15].ToUpperInvariant();
}
