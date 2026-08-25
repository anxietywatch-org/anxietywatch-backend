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

    [Fact]
    public async Task GetAcceptedCaregiverRelationships_ReturnsOnlyActiveFamilyMemberRelationships()
    {
        var caregiverId = Guid.NewGuid();
        var otherCaregiverId = Guid.NewGuid();
        var visiblePatientId = Guid.NewGuid();
        await AddAcceptedAsync(visiblePatientId, caregiverId);
        await tokens.TryAddAsync(NewToken(Guid.NewGuid()), 10);
        await AddAcceptedAsync(Guid.NewGuid(), caregiverId, "self");
        await AddAcceptedAsync(Guid.NewGuid(), caregiverId, "patient");
        await AddAcceptedAsync(Guid.NewGuid(), otherCaregiverId);
        var revoked = await AddAcceptedAsync(Guid.NewGuid(), caregiverId);
        await tokens.TryRevokeAsync(revoked.Id);

        var relationships = await tokens.GetAcceptedCaregiverRelationshipsAsync(caregiverId);

        relationships.Should().ContainSingle();
        relationships[0].PatientId.Should().Be(visiblePatientId);
        relationships[0].Role.Should().Be("family_member");
    }

    [Fact]
    public async Task GetAcceptedCaregiverRelationships_IncludesAcceptedRelationshipPastOriginalTokenExpiry()
    {
        var caregiverId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var acceptedAt = DateTimeOffset.UtcNow;
        var token = new LinkToken(Guid.NewGuid(), patientId, Code(), "family_member", acceptedAt.AddMinutes(5));
        (await tokens.TryAddAsync(token, 10)).Should().BeTrue();
        (await tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, acceptedAt)).Should().BeTrue();

        var afterOriginalExpiry = acceptedAt.AddMinutes(6);
        var relationships = await tokens.GetAcceptedCaregiverRelationshipsAsync(caregiverId);

        relationships.Should().ContainSingle(relationship =>
            relationship.PatientId == patientId && relationship.LinkedAt < afterOriginalExpiry);
    }

    [Fact]
    public async Task GetAcceptedCaregiverRelationships_DeduplicatesByPatientWithEarliestLinkedAtAndOrdersDescending()
    {
        var caregiverId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var olderPatientId = Guid.NewGuid();
        var earliest = DateTimeOffset.UtcNow.AddMinutes(-10);
        var duplicateLater = DateTimeOffset.UtcNow.AddMinutes(-1);
        var older = DateTimeOffset.UtcNow.AddMinutes(-20);
        await AddAcceptedAsync(patientId, caregiverId, "family_member", earliest);
        await AddAcceptedAsync(patientId, caregiverId, "family_member", duplicateLater);
        await AddAcceptedAsync(olderPatientId, caregiverId, "family_member", older);

        var relationships = await tokens.GetAcceptedCaregiverRelationshipsAsync(caregiverId);

        relationships.Select(relationship => relationship.PatientId).Should().Equal(patientId, olderPatientId);
        relationships[0].LinkedAt.Should().Be(earliest);
    }

    private async Task<LinkToken> AddAcceptedAsync(
        Guid patientId,
        Guid caregiverId,
        string role = "family_member",
        DateTimeOffset? acceptedAt = null)
    {
        var token = NewToken(patientId, role);
        (await tokens.TryAddAsync(token, 10)).Should().BeTrue();
        (await tokens.TryAcceptAsync(token.Id, token.Code, caregiverId, acceptedAt ?? DateTimeOffset.UtcNow)).Should().BeTrue();
        return token;
    }

    private static LinkToken NewToken(Guid patientId, string role = "family_member") =>
        new(Guid.NewGuid(), patientId, Code(), role, DateTimeOffset.UtcNow.AddDays(30));

    private static string Code() => $"AW-{Guid.NewGuid():N}"[..15].ToUpperInvariant();
}
