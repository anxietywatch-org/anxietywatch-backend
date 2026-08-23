using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Authentication;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Persistence;
using AnxietyWatch.Infrastructure.Security;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Infrastructure;

public sealed class InMemoryConcurrencyTests
{
    [Fact]
    public async Task PasswordChange_ShouldInvalidatePreviouslyReadLoginSnapshot()
    {
        var repository = new InMemoryUserRepository();
        var user = new User(Guid.NewGuid(), "Test User", "test@example.test", "old-hash", "free");
        await repository.AddAsync(user);
        var loginSnapshot = await repository.GetByEmailAsync(user.Email);

        (await repository.UpdatePasswordAsync(user.Id, "new-hash")).Should().BeTrue();

        (await repository.RegisterFailedLoginAsync(
            user.Id,
            DateTimeOffset.UtcNow,
            loginSnapshot!.PasswordHash)).Should().BeNull();

        var result = await repository.RegisterSuccessfulLoginAsync(
            user.Id,
            DateTimeOffset.UtcNow,
            loginSnapshot!.Version,
            loginSnapshot.PasswordHash);
        result.Should().BeNull();
        var stored = await repository.GetByIdAsync(user.Id);
        stored!.PasswordHash.Should().Be("new-hash");
        stored.FailedLoginAttempts.Should().Be(0);
        stored.SecurityVersion.Should().Be(1);
    }

    [Fact]
    public async Task VerificationDeliveryFailure_ShouldRollbackTokenAndCooldown()
    {
        var repository = new InMemoryUserRepository();
        var user = new User(Guid.NewGuid(), "Test User", "verify@example.test", "hash", "free");
        await repository.AddAsync(user);
        var currentUser = new StubCurrentUser(user.Id, user.Email);
        var clock = new StubClock(DateTimeOffset.UtcNow);
        var failingHandler = new ResendVerificationEmailCommandHandler(
            currentUser,
            repository,
            clock,
            new ThrowingEmailSender(),
            new StubLinkFactory());

        await FluentActions.Invoking(() => failingHandler.Handle(
                new ResendVerificationEmailCommand(),
                CancellationToken.None))
            .Should().ThrowAsync<ServiceUnavailableException>();
        (await repository.GetByIdAsync(user.Id))!.LastVerificationEmailSentAt.Should().BeNull();

        var successfulSender = new TestEmailSender();
        var retryHandler = new ResendVerificationEmailCommandHandler(
            currentUser,
            repository,
            clock,
            successfulSender,
            new StubLinkFactory());
        (await retryHandler.Handle(new ResendVerificationEmailCommand(), CancellationToken.None))
            .Should().Be("Verification email sent");
        successfulSender.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task LinkTokenRotation_ShouldAllowOnlyOneWinnerForTheSameExpectedCode()
    {
        var repository = new InMemoryLinkTokenRepository();
        var ownerId = Guid.NewGuid();
        var token = new LinkToken(Guid.NewGuid(), ownerId, "AW-OLD1-OLD2-OLD3", "self", DateTimeOffset.UtcNow.AddDays(30));
        (await repository.TryAddAsync(token, 1)).Should().BeTrue();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            repository.TryRotateAsync(
                token.Id,
                ownerId,
                token.Code,
                $"AW-NEW1-NEW2-{index:0000}",
                DateTimeOffset.UtcNow.AddDays(30))));

        var winners = attempts.Where(result => result is not null).ToArray();
        winners.Should().ContainSingle();
        var stored = await repository.GetByIdAsync(token.Id);
        stored!.Code.Should().Be(winners[0]!.Code);
        stored.Id.Should().Be(token.Id);
        stored.Role.Should().Be(token.Role);
    }

    [Fact]
    public async Task LinkTokenRotationAndAccept_ShouldAllowOnlyOneLifecycleTransition()
    {
        var repository = new InMemoryLinkTokenRepository();
        var ownerId = Guid.NewGuid();
        var acceptedBy = Guid.NewGuid();
        var acceptedAt = DateTimeOffset.UtcNow;
        var token = new LinkToken(Guid.NewGuid(), ownerId, "AW-OLD1-OLD2-OLD3", "self", acceptedAt.AddDays(30));
        (await repository.TryAddAsync(token, 1)).Should().BeTrue();

        var rotateTask = repository.TryRotateAsync(token.Id, ownerId, token.Code, "AW-NEW1-NEW2-NEW3", acceptedAt.AddDays(30));
        var acceptTask = repository.TryAcceptAsync(token.Id, token.Code, acceptedBy, acceptedAt);
        await Task.WhenAll(rotateTask, acceptTask);

        var rotated = await rotateTask;
        var accepted = await acceptTask;
        ((rotated is not null ? 1 : 0) + (accepted ? 1 : 0)).Should().Be(1);
        var stored = await repository.GetByIdAsync(token.Id);
        if (rotated is not null)
        {
            stored!.Status.Should().Be(TokenStatus.Pending);
            stored.Code.Should().Be(rotated.Code);
            (await repository.TryAcceptAsync(token.Id, token.Code, acceptedBy, acceptedAt.AddSeconds(1))).Should().BeFalse();
        }
        else
        {
            stored!.Status.Should().Be(TokenStatus.Accepted);
            stored.AcceptedBy.Should().Be(acceptedBy);
            stored.AcceptedAt.Should().Be(acceptedAt);
            (await repository.TryRotateAsync(token.Id, ownerId, token.Code, "AW-LATE-LATE-LATE", acceptedAt.AddDays(30)))
                .Should().BeNull();
        }
    }

    [Fact]
    public async Task LinkTokenRotationAndDelete_ShouldAllowOnlyOneLifecycleTransition()
    {
        var repository = new InMemoryLinkTokenRepository();
        var ownerId = Guid.NewGuid();
        var token = new LinkToken(Guid.NewGuid(), ownerId, "AW-OLD1-OLD2-OLD3", "self", DateTimeOffset.UtcNow.AddDays(30));
        (await repository.TryAddAsync(token, 1)).Should().BeTrue();

        var rotateTask = repository.TryRotateAsync(token.Id, ownerId, token.Code, "AW-NEW1-NEW2-NEW3", DateTimeOffset.UtcNow.AddDays(30));
        var deleteTask = repository.TryDeleteAsync(token.Id, token.Code);
        await Task.WhenAll(rotateTask, deleteTask);

        var rotated = await rotateTask;
        var deleted = await deleteTask;
        ((rotated is not null ? 1 : 0) + (deleted ? 1 : 0)).Should().Be(1);
        var stored = await repository.GetByIdAsync(token.Id);
        if (rotated is not null)
        {
            stored!.Status.Should().Be(TokenStatus.Pending);
            stored.Code.Should().Be(rotated.Code);
            (await repository.TryDeleteAsync(token.Id, token.Code)).Should().BeFalse();
        }
        else
        {
            stored!.Status.Should().Be(TokenStatus.Deleted);
            (await repository.TryRotateAsync(token.Id, ownerId, token.Code, "AW-LATE-LATE-LATE", DateTimeOffset.UtcNow.AddDays(30)))
                .Should().BeNull();
        }
    }

    private sealed class StubCurrentUser(Guid userId, string email) : ICurrentUser
    {
        public Guid UserId { get; } = userId;
        public string? Email { get; } = email;
        public string? PlanId => "free";
        public string? JwtId => null;
        public DateTimeOffset? TokenExpiresAt => null;
        public bool IsAuthenticated => true;
    }

    private sealed class StubClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class StubLinkFactory : IEmailVerificationLinkFactory
    {
        public string Create(string token) => $"https://mangoon.xyz/verify-email#token={token}";
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(
            string recipientEmail,
            string subject,
            string body,
            CancellationToken cancellationToken = default) =>
            throw new EmailDeliveryException("Delivery failed.");

        public Task SendHtmlAsync(
            string recipientEmail,
            string subject,
            string htmlBody,
            CancellationToken cancellationToken = default) =>
            throw new EmailDeliveryException("Delivery failed.");
    }
}
