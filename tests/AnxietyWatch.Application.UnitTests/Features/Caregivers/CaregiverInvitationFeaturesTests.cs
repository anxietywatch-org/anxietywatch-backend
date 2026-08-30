using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Persistence;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Caregivers;

public sealed class CaregiverInvitationFeaturesTests
{
    private static readonly Guid OwnerId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid PatientId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid CaregiverId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_UsesRoutePatientAndRequiresOwnerMembership()
    {
        var current = Current(OwnerId);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(new User(PatientId, "Patient", "p@example.test", "hash", "free"));
        var authorizer = Substitute.For<AnxietyWatch.Application.Features.FamilyPlans.IFamilyPlanPatientAuthorizer>();
        authorizer.CanManagePatientAsync(OwnerId, PatientId, Arg.Any<CancellationToken>()).Returns(true);
        var repository = new InMemoryCaregiverInvitationRepository();
        var clock = Substitute.For<ISystemClock>(); clock.UtcNow.Returns(Now);

        var response = await new CreateCaregiverInvitationHandler(current, users, authorizer, repository, clock).Handle(new(PatientId), default);

        response.Code.Should().NotBeNullOrWhiteSpace();
        (await repository.GetByCodeAsync(response.Code))!.IssuedByUserId.Should().Be(OwnerId);
        (await repository.GetByCodeAsync(response.Code))!.TargetPatientId.Should().Be(PatientId);
    }

    [Fact]
    public async Task Accept_BindsCurrentCaregiverToInvitationPatient_AndIsIdempotent()
    {
        var invitations = new InMemoryCaregiverInvitationRepository();
        var links = new InMemoryCaregiverPatientLinkRepository();
        var invitation = new CaregiverInvitation(Guid.NewGuid(), OwnerId, PatientId, "invite-1", Now.AddDays(1));
        await invitations.AddAsync(invitation);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(PatientId, Arg.Any<CancellationToken>()).Returns(new User(PatientId, "Patient", "p@example.test", "hash", "free"));
        var clock = Substitute.For<ISystemClock>(); clock.UtcNow.Returns(Now);
        var handler = new AcceptCaregiverInvitationHandler(Current(CaregiverId), users, invitations, links, clock);

        var first = await handler.Handle(new("invite-1"), default);
        var second = await handler.Handle(new("invite-1"), default);

        first.PatientId.Should().Be(PatientId);
        second.PatientId.Should().Be(PatientId);
        (await links.ListByCaregiverAsync(CaregiverId)).Should().ContainSingle();
        (await links.IsLinkedAsync(OwnerId, CaregiverId)).Should().BeFalse();
    }

    [Fact]
    public async Task Create_RejectsPatientOutsideOwnersMembership()
    {
        var current = Current(OwnerId);
        var users = Substitute.For<IUserRepository>();
        var authorizer = Substitute.For<AnxietyWatch.Application.Features.FamilyPlans.IFamilyPlanPatientAuthorizer>();
        authorizer.CanManagePatientAsync(OwnerId, PatientId, Arg.Any<CancellationToken>()).Returns(false);
        var act = () => new CreateCaregiverInvitationHandler(current, users, authorizer, new InMemoryCaregiverInvitationRepository(), Substitute.For<ISystemClock>()).Handle(new(PatientId), default);
        await act.Should().ThrowAsync<ForbiddenException>();
        await users.DidNotReceive().GetByIdAsync(PatientId, Arg.Any<CancellationToken>());
    }

    private static ICurrentUser Current(Guid id)
    {
        var current = Substitute.For<ICurrentUser>(); current.IsAuthenticated.Returns(true); current.UserId.Returns(id); return current;
    }
}
