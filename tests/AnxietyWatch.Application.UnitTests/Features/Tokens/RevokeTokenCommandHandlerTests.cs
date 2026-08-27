using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Features.Tokens;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Caregivers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Tokens;

public sealed class RevokeTokenCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILinkTokenRepository _tokens = Substitute.For<ILinkTokenRepository>();
    private readonly ISystemClock _clock = Substitute.For<ISystemClock>();
    private readonly ICaregiverRelationshipAuditRepository _audit = Substitute.For<ICaregiverRelationshipAuditRepository>();
    private readonly RevokeTokenCommandHandler _handler;

    public RevokeTokenCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.Parse("704f39ff-2364-401c-9509-bf796dd7a635"));
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _handler = new RevokeTokenCommandHandler(
            _currentUser, _tokens, _clock, _audit, NullLogger<RevokeTokenCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenTokenIsAccepted_ShouldMarkItDeleted()
    {
        var id = Guid.NewGuid();
        var token = LinkToken.Restore(
            id,
            _currentUser.UserId,
            "AW-TEST-TEST-TEST",
            "self",
            DateTimeOffset.UtcNow.AddDays(30),
            TokenStatus.Accepted,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        _tokens.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryRevokeAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(new RevokeTokenCommand(id), CancellationToken.None);

        result.Should().BeTrue();
        await _tokens.Received(1).TryRevokeAsync(id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenFamilyMemberRelationshipIsRevoked_ShouldAppendOneAuditEvent()
    {
        var id = Guid.NewGuid();
        var caregiverId = Guid.NewGuid();
        var patientId = _currentUser.UserId;
        var token = LinkToken.Restore(
            id, patientId, "AW-TEST-TEST-TEST", "family_member",
            DateTimeOffset.UtcNow.AddDays(30), TokenStatus.Accepted,
            caregiverId, DateTimeOffset.UtcNow);
        _tokens.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryRevokeAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        await _handler.Handle(new RevokeTokenCommand(id), CancellationToken.None);

        await _audit.Received(1).AppendAsync(
            Arg.Is<CaregiverRelationshipAuditEvent>(item =>
                item.PatientId == patientId && item.CaregiverId == caregiverId &&
                item.SourceTokenId == id && item.Action == CaregiverRelationshipAuditAction.Revoked),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenRevokeFails_ShouldThrowConflict()
    {
        var id = Guid.NewGuid();
        var token = LinkToken.Restore(
            id,
            _currentUser.UserId,
            "AW-TEST-TEST-TEST",
            "self",
            DateTimeOffset.UtcNow.AddDays(30),
            TokenStatus.Accepted,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        _tokens.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);
        _tokens.TryRevokeAsync(id, Arg.Any<CancellationToken>()).Returns(false);

        var act = async () => await _handler.Handle(new RevokeTokenCommand(id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Handle_WhenTokenIsPending_ShouldThrowConflict()
    {
        var id = Guid.NewGuid();
        var token = LinkToken.Restore(
            id,
            _currentUser.UserId,
            "AW-TEST-TEST-TEST",
            "self",
            DateTimeOffset.UtcNow.AddDays(30),
            TokenStatus.Pending,
            null,
            null);
        _tokens.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        var act = async () => await _handler.Handle(new RevokeTokenCommand(id), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        token.Status.Should().Be(TokenStatus.Pending);
    }

    [Fact]
    public async Task Handle_WhenTokenDoesNotExist_ShouldThrowNotFound()
    {
        _tokens.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((LinkToken?)null);

        var act = async () => await _handler.Handle(new RevokeTokenCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenTokenBelongsToAnotherUser_ShouldThrowForbidden()
    {
        var id = Guid.NewGuid();
        var token = LinkToken.Restore(
            id,
            Guid.NewGuid(),
            "AW-TEST-TEST-TEST",
            "self",
            DateTimeOffset.UtcNow.AddDays(30),
            TokenStatus.Accepted,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        _tokens.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(token);

        var act = async () => await _handler.Handle(new RevokeTokenCommand(id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_WhenUserIsNotAuthenticated_ShouldThrowUnauthorized()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var act = async () => await _handler.Handle(new RevokeTokenCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedApplicationException>();
    }
}
