using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Tokens;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Caregivers;

public sealed class CaregiverRelationshipResolverTests
{
    [Fact]
    public async Task IsLinkedAsync_UsesExplicitOrLegacyRelationship()
    {
        var patient = Guid.NewGuid();
        var explicitCaregiver = Guid.NewGuid();
        var legacyCaregiver = Guid.NewGuid();
        var none = Guid.NewGuid();
        var (resolver, tokens, links) = Create(patient);
        links.IsLinkedAsync(explicitCaregiver, patient, Arg.Any<CancellationToken>()).Returns(true);
        tokens.HasAcceptedCaregiverRelationshipAsync(patient, legacyCaregiver, Arg.Any<CancellationToken>()).Returns(true);

        (await resolver.IsLinkedAsync(explicitCaregiver, patient)).Should().BeTrue();
        (await resolver.IsLinkedAsync(legacyCaregiver, patient)).Should().BeTrue();
        (await resolver.IsLinkedAsync(none, patient)).Should().BeFalse();
    }

    [Fact]
    public async Task ListCaregiverIdsAsync_CombinesSourcesAndDeduplicatesHybrid()
    {
        var patient = Guid.NewGuid();
        var explicitCaregiver = Guid.NewGuid();
        var legacyCaregiver = Guid.NewGuid();
        var hybrid = Guid.NewGuid();
        var (resolver, tokens, links) = Create(patient);
        links.ListByPatientAsync(patient, Arg.Any<CancellationToken>()).Returns([
            new CaregiverPatientLink(Guid.NewGuid(), explicitCaregiver, patient, DateTimeOffset.UtcNow, null),
            new CaregiverPatientLink(Guid.NewGuid(), hybrid, patient, DateTimeOffset.UtcNow, null)]);
        tokens.GetAsync(patient, Arg.Any<CancellationToken>()).Returns([
            LinkToken.Restore(Guid.NewGuid(), patient, "legacy", "family_member", DateTimeOffset.UtcNow.AddDays(1), TokenStatus.Accepted, legacyCaregiver, DateTimeOffset.UtcNow),
            LinkToken.Restore(Guid.NewGuid(), patient, "hybrid", "family_member", DateTimeOffset.UtcNow.AddDays(1), TokenStatus.Accepted, hybrid, DateTimeOffset.UtcNow)]);

        var result = await resolver.ListCaregiverIdsAsync(patient);

        result.Should().BeEquivalentTo([explicitCaregiver, legacyCaregiver, hybrid]);
    }

    [Fact]
    public async Task ListCaregiverIdsAsync_IgnoresNonAcceptedOrNonFamilyTokens()
    {
        var patient = Guid.NewGuid();
        var caregiver = Guid.NewGuid();
        var (resolver, tokens, _) = Create(patient);
        tokens.GetAsync(patient, Arg.Any<CancellationToken>()).Returns([
            LinkToken.Restore(Guid.NewGuid(), patient, "pending", "family_member", DateTimeOffset.UtcNow.AddDays(1), TokenStatus.Pending, caregiver, null),
            LinkToken.Restore(Guid.NewGuid(), patient, "patient", "patient", DateTimeOffset.UtcNow.AddDays(1), TokenStatus.Accepted, caregiver, DateTimeOffset.UtcNow)]);

        (await resolver.ListCaregiverIdsAsync(patient)).Should().BeEmpty();
    }

    private static (CaregiverRelationshipResolver Resolver, ILinkTokenRepository Tokens, ICaregiverPatientLinkRepository Links) Create(Guid patient)
    {
        var tokens = Substitute.For<ILinkTokenRepository>();
        tokens.GetAsync(patient, Arg.Any<CancellationToken>()).Returns([]);
        var links = Substitute.For<ICaregiverPatientLinkRepository>();
        links.ListByPatientAsync(patient, Arg.Any<CancellationToken>()).Returns([]);
        return (new CaregiverRelationshipResolver(tokens, links), tokens, links);
    }
}
