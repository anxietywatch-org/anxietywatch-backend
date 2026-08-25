using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Infrastructure.Persistence;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class InMemoryCaregiverRelationshipAuthorizationTests
{
    private readonly InMemoryLinkTokenRepository tokens = new();

    [Fact]
    public async Task AcceptedFamilyMemberLink_AllowsCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        await AddAcceptedAsync(patientId, caregiverId);

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkedCaregiver_DoesNotGrantCaregiverAccess()
    {
        (await tokens.HasAcceptedCaregiverRelationshipAsync(Guid.NewGuid(), Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticatedUserGuessesPatientId_DoesNotGrantCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        await AddAcceptedAsync(patientId, Guid.NewGuid());

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task PendingToken_DoesNotGrantCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        await tokens.TryAddAsync(NewToken(patientId), 10);

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeFalse();
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
    public async Task AcceptedRelationship_SurvivesOriginalTokenExpiryUntilRevoked()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var token = new LinkToken(Guid.NewGuid(), patientId, Code(), "family_member", now.AddMinutes(5));
        (await tokens.TryAddAsync(token, 10)).Should().BeTrue();
        (await tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, now)).Should().BeTrue();

        var afterOriginalExpiry = now.AddMinutes(6);
        var accepted = await tokens.GetByIdAsync(token.Id);
        accepted!.ExpiresAt.Should().BeBefore(afterOriginalExpiry);
        accepted.Status.Should().Be(TokenStatus.Accepted);
        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeTrue();

        (await tokens.TryRevokeAsync(token.Id)).Should().BeTrue();

        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeFalse();
    }

    [Fact]
    public async Task ExpiredStatus_DoesNotGrantCaregiverAccess()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var token = LinkToken.Restore(
            Guid.NewGuid(),
            patientId,
            Code(),
            "family_member",
            DateTimeOffset.UtcNow.AddDays(30),
            TokenStatus.Expired,
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
    public async Task RevocationTakesEffectImmediatelyWithoutNewLogin()
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var token = await AddAcceptedAsync(patientId, caregiverId);
        (await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId)).Should().BeTrue();

        await tokens.TryRevokeAsync(token.Id);

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

    [Theory]
    [InlineData("patient")]
    [InlineData("unknown")]
    [InlineData("FAMILY_MEMBER")]
    public async Task NonExactFamilyMemberRole_DoesNotCreateCaregiverAccess(string role)
    {
        var patientId = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        await AddAcceptedAsync(patientId, caregiverId, role);

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
