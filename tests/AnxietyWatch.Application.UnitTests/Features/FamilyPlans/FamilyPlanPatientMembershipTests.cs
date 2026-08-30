using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.FamilyPlans;
using AnxietyWatch.Application.Features.Tokens;
using AnxietyWatch.Domain.FamilyPlans;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Persistence;
using AnxietyWatch.Infrastructure.Security;
using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Application.UnitTests.Features.FamilyPlans;

public sealed class FamilyPlanPatientMembershipTests
{
    [Fact]
    public async Task EnsureMembership_IsIdempotentAndAuthorizesOnlyTheStoredPair()
    {
        var repository = new InMemoryFamilyPlanPatientMembershipRepository();
        var ownerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var first = await repository.EnsureMembershipAsync(ownerId, patientId, null, DateTimeOffset.UtcNow);
        var second = await repository.EnsureMembershipAsync(ownerId, patientId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(1));

        second.Id.Should().Be(first.Id);
        (await repository.ListPatientsAsync(ownerId)).Should().ContainSingle();
        (await repository.CanManagePatientAsync(ownerId, patientId)).Should().BeTrue();
        (await repository.CanManagePatientAsync(Guid.NewGuid(), patientId)).Should().BeFalse();
        (await repository.CanManagePatientAsync(ownerId, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task Authorizer_RequiresFamilyOwnerAndActiveMembership()
    {
        var users = new InMemoryUserRepository();
        var memberships = new InMemoryFamilyPlanPatientMembershipRepository();
        var ownerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        await users.AddAsync(new User(ownerId, "Owner", "owner@example.test", "hash", "family"));
        await users.AddAsync(new User(patientId, "Patient", "patient@example.test", "hash", "free"));
        await memberships.EnsureMembershipAsync(ownerId, patientId, null, DateTimeOffset.UtcNow);
        var authorizer = new FamilyPlanPatientAuthorizer(users, memberships);

        (await authorizer.CanManagePatientAsync(ownerId, patientId)).Should().BeTrue();
        (await authorizer.CanManagePatientAsync(Guid.NewGuid(), patientId)).Should().BeFalse();
    }

    [Fact]
    public async Task Authorizer_FamilyPlanWithoutMembershipCannotManagePatient()
    {
        var ownerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var users = new InMemoryUserRepository();
        await users.AddAsync(new User(ownerId, "Owner", "owner-without-membership@example.test", "hash", "family"));

        var authorizer = new FamilyPlanPatientAuthorizer(users, new InMemoryFamilyPlanPatientMembershipRepository());

        (await authorizer.CanManagePatientAsync(ownerId, patientId)).Should().BeFalse();
    }

    [Fact]
    public async Task Reconciler_UsesAcceptedPatientTokenOwnerAsOwnerAndAcceptedByAsPatient()
    {
        var tokens = new InMemoryLinkTokenRepository();
        var users = new InMemoryUserRepository();
        var memberships = new InMemoryFamilyPlanPatientMembershipRepository();
        var clock = Substitute.For<ISystemClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        var ownerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        await users.AddAsync(new User(ownerId, "Owner", "owner@example.test", "hash", "family"));
        await users.AddAsync(new User(patientId, "Patient", "patient@example.test", "hash", "free"));
        var token = LinkToken.Restore(Guid.NewGuid(), ownerId, "AW-TEST", "patient", now.AddHours(1), TokenStatus.Accepted, patientId, now);
        await tokens.TryAddAsync(token, 10);

        var reconciler = new FamilyPlanPatientMembershipReconciler(tokens, users, memberships, clock, Substitute.For<ILogger<FamilyPlanPatientMembershipReconciler>>());
        var first = await reconciler.ReconcileAcceptedPatientTokensAsync();
        var second = await reconciler.ReconcileAcceptedPatientTokensAsync();

        first.Should().Be(1);
        second.Should().Be(1);
        var stored = await memberships.ListPatientsAsync(ownerId);
        stored.Should().ContainSingle().Which.PatientUserId.Should().Be(patientId);
        stored[0].OwnerUserId.Should().Be(ownerId);
    }

    [Fact]
    public async Task Reconciler_IgnoresNonPatientUnacceptedAndNonFamilyTokens()
    {
        var tokens = new InMemoryLinkTokenRepository();
        var users = new InMemoryUserRepository();
        var memberships = new InMemoryFamilyPlanPatientMembershipRepository();
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        var ownerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        await users.AddAsync(new User(ownerId, "Owner", "owner@example.test", "hash", "family"));
        await users.AddAsync(new User(patientId, "Patient", "patient@example.test", "hash", "free"));
        var familyToken = LinkToken.Restore(Guid.NewGuid(), ownerId, "AW-FAMILY", "family_member", DateTimeOffset.UtcNow.AddHours(1), TokenStatus.Accepted, patientId, DateTimeOffset.UtcNow);
        var pendingToken = new LinkToken(Guid.NewGuid(), ownerId, "AW-PENDING", "patient", DateTimeOffset.UtcNow.AddHours(1));
        var freeOwner = Guid.NewGuid();
        await users.AddAsync(new User(freeOwner, "Free", "free@example.test", "hash", "free"));
        var nonFamilyToken = LinkToken.Restore(Guid.NewGuid(), freeOwner, "AW-FREE", "patient", DateTimeOffset.UtcNow.AddHours(1), TokenStatus.Accepted, patientId, DateTimeOffset.UtcNow);
        await tokens.TryAddAsync(familyToken, 10);
        await tokens.TryAddAsync(pendingToken, 10);
        await tokens.TryAddAsync(nonFamilyToken, 10);

        var reconciler = new FamilyPlanPatientMembershipReconciler(tokens, users, memberships, clock, Substitute.For<ILogger<FamilyPlanPatientMembershipReconciler>>());

        (await reconciler.ReconcileAcceptedPatientTokensAsync()).Should().Be(0);
        (await memberships.ListPatientsAsync(ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task FamilyPatientsQuery_ReturnsOnlyPatientsFromTheOwnersMemberships()
    {
        var ownerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerId);
        var users = new InMemoryUserRepository();
        var memberships = new InMemoryFamilyPlanPatientMembershipRepository();
        await users.AddAsync(new User(ownerId, "Owner", "owner-query@example.test", "hash", "family"));
        await users.AddAsync(new User(patientId, "Patient P", "patient-query@example.test", "hash", "free"));
        await memberships.EnsureMembershipAsync(ownerId, patientId, null, DateTimeOffset.UtcNow);

        var handler = new GetFamilyPlanPatientsQueryHandler(currentUser, users, memberships);

        var result = await handler.Handle(new GetFamilyPlanPatientsQuery(), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Be(new FamilyPlanPatientResponse(patientId.ToString(), "Patient P"));
    }

    [Fact]
    public async Task FamilyPatientsQuery_RejectsNonFamilyOwner()
    {
        var ownerId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerId);
        var users = new InMemoryUserRepository();
        await users.AddAsync(new User(ownerId, "Owner", "owner-free@example.test", "hash", "free"));
        var handler = new GetFamilyPlanPatientsQueryHandler(currentUser, users, new InMemoryFamilyPlanPatientMembershipRepository());

        var act = () => handler.Handle(new GetFamilyPlanPatientsQuery(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData("free")]
    [InlineData("individual")]
    [InlineData("professional")]
    [InlineData("family")]
    public async Task CreatePatientToken_PreservesLegacyQuotaBehavior(string planId)
    {
        var ownerId = Guid.NewGuid();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(ownerId);
        var users = new InMemoryUserRepository();
        await users.AddAsync(new User(ownerId, "Owner", $"{planId}-{ownerId}@example.test", "hash", planId));
        var clock = Substitute.For<ISystemClock>();
        clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var response = await new CreateTokenCommandHandler(currentUser, new InMemoryLinkTokenRepository(), users, clock)
            .Handle(new CreateTokenCommand("patient"), CancellationToken.None);

        response.Role.Should().Be("patient");
    }

    [Fact]
    public async Task PatientAcceptance_CreatesOwnerToAcceptedAccountMembership_AndTokenLifecycleIsSeparate()
    {
        var ownerId = Guid.NewGuid();
        var tokens = new InMemoryLinkTokenRepository();
        var users = new InMemoryUserRepository();
        var memberships = new InMemoryFamilyPlanPatientMembershipRepository();
        var currentUser = Substitute.For<ICurrentUser>();
        var clock = Substitute.For<ISystemClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        await users.AddAsync(new User(ownerId, "Owner", "patient-token-owner@example.test", "hash", "family"));
        var token = new LinkToken(Guid.NewGuid(), ownerId, "AW-PATIENT", "patient", now.AddHours(1));
        await tokens.TryAddAsync(token, 10);
        var jwt = Substitute.For<IJwtTokenService>();
        jwt.Create(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>())
            .Returns(new JwtToken("redacted-test-token", now.AddHours(1), Guid.NewGuid().ToString()));

        var response = await new TokenRedeemCommandHandler(tokens, users, jwt, clock, memberships, currentUser)
            .Handle(new TokenRedeemCommand(token.Code, "device"), CancellationToken.None);

        response.Role.Should().Be("patient");
        var patientId = Guid.Parse(response.User.Id);
        patientId.Should().Be(token.Id);
        (await memberships.CanManagePatientAsync(ownerId, patientId)).Should().BeTrue();
        (await tokens.TryRevokeAsync(token.Id)).Should().BeTrue();
        (await memberships.CanManagePatientAsync(ownerId, patientId)).Should().BeTrue();
    }

    [Theory]
    [InlineData("self")]
    [InlineData("family_member")]
    public async Task NonPatientTokenAcceptance_DoesNotCreateFamilyMembership(string role)
    {
        var ownerId = Guid.NewGuid();
        var tokens = new InMemoryLinkTokenRepository();
        var users = new InMemoryUserRepository();
        var memberships = new InMemoryFamilyPlanPatientMembershipRepository();
        var currentUser = Substitute.For<ICurrentUser>();
        var clock = Substitute.For<ISystemClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        await users.AddAsync(new User(ownerId, "Owner", $"{role}-{ownerId}@example.test", "hash", "family"));
        var token = new LinkToken(Guid.NewGuid(), ownerId, $"AW-{role}", role, now.AddHours(1));
        await tokens.TryAddAsync(token, 10);
        var jwt = Substitute.For<IJwtTokenService>();
        jwt.Create(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>())
            .Returns(new JwtToken("redacted-test-token", now.AddHours(1), Guid.NewGuid().ToString()));

        await new TokenRedeemCommandHandler(tokens, users, jwt, clock, memberships, currentUser)
            .Handle(new TokenRedeemCommand(token.Code, "device"), CancellationToken.None);

        (await memberships.ListPatientsAsync(ownerId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Reconciler_ContinuesAfterOneMembershipFailure()
    {
        var tokens = Substitute.For<ILinkTokenRepository>();
        var users = Substitute.For<IUserRepository>();
        var memberships = Substitute.For<IFamilyPlanPatientMembershipRepository>();
        var clock = Substitute.For<ISystemClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        var ownerId = Guid.NewGuid();
        var firstPatientId = Guid.NewGuid();
        var secondPatientId = Guid.NewGuid();
        var firstToken = LinkToken.Restore(Guid.NewGuid(), ownerId, "AW-FIRST", "patient", now.AddHours(1), TokenStatus.Accepted, firstPatientId, now);
        var secondToken = LinkToken.Restore(Guid.NewGuid(), ownerId, "AW-SECOND", "patient", now.AddHours(1), TokenStatus.Accepted, secondPatientId, now);
        tokens.GetAcceptedPatientTokensAsync(Arg.Any<CancellationToken>()).Returns([firstToken, secondToken]);
        users.GetByIdAsync(ownerId, Arg.Any<CancellationToken>()).Returns(new User(ownerId, "Owner", "owner-reconcile@example.test", "hash", "family"));
        users.GetByIdAsync(firstPatientId, Arg.Any<CancellationToken>()).Returns(new User(firstPatientId, "First", "first@example.test", "hash", "free"));
        users.GetByIdAsync(secondPatientId, Arg.Any<CancellationToken>()).Returns(new User(secondPatientId, "Second", "second@example.test", "hash", "free"));
        memberships.EnsureMembershipAsync(Arg.Any<Guid>(), firstPatientId, Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FamilyPlanPatientMembership>(new InvalidOperationException("simulated")));
        memberships.EnsureMembershipAsync(Arg.Any<Guid>(), secondPatientId, Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new FamilyPlanPatientMembership(Guid.NewGuid(), ownerId, secondPatientId, now, secondToken.Id)));

        var reconciler = new FamilyPlanPatientMembershipReconciler(tokens, users, memberships, clock, Substitute.For<ILogger<FamilyPlanPatientMembershipReconciler>>());

        (await reconciler.ReconcileAcceptedPatientTokensAsync()).Should().Be(1);
        await memberships.Received(1).EnsureMembershipAsync(ownerId, secondPatientId, secondToken.Id, now, Arg.Any<CancellationToken>());
    }
}
