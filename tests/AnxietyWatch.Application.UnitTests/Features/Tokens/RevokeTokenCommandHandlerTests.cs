using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Tokens;
using AnxietyWatch.Domain.Tokens;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Tokens;

public sealed class RevokeTokenCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ILinkTokenRepository _tokens = Substitute.For<ILinkTokenRepository>();
    private readonly RevokeTokenCommandHandler _handler;

    public RevokeTokenCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.Parse("704f39ff-2364-401c-9509-bf796dd7a635"));
        _handler = new RevokeTokenCommandHandler(_currentUser, _tokens);
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

        var result = await _handler.Handle(new RevokeTokenCommand(id), CancellationToken.None);

        result.Should().BeTrue();
        token.Status.Should().Be(TokenStatus.Deleted);
        await _tokens.Received(1).UpdateAsync(token, Arg.Any<CancellationToken>());
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