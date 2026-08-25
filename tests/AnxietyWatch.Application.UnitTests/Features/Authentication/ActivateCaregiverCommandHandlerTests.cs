using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Authentication;
using AnxietyWatch.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Authentication;

public sealed class ActivateCaregiverCommandHandlerTests
{
    [Theory]
    [InlineData("patient", "patient@example.test")]
    [InlineData("self", "self@example.test")]
    [InlineData("family_member", "family@example.test")]
    [InlineData("family_member", "caregiver+already@example.test")]
    public async Task Handle_ShouldRejectAccountsThatAreNotTemporaryCaregivers(string role, string email)
    {
        var user = new User(Guid.NewGuid(), "User", email, "placeholder", "free", role);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        users.GetByEmailAsync("new@example.test", Arg.Any<CancellationToken>()).Returns((User?)null);
        users.TryActivateCaregiverAsync(
                user.Id,
                user.Version,
                user.Email,
                "new@example.test",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var handler = CreateHandler(users, user.Id);

        var act = () => handler.Handle(
            new ActivateCaregiverCommand("new@example.test", "Password1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("The caregiver account is already activated or is not eligible.");
    }

    [Fact]
    public async Task Handle_ShouldAllowOnlyTemporaryInternalCaregiver()
    {
        var user = new User(
            Guid.NewGuid(),
            "Cuidador",
            "caregiver+temporary@device.anxietywatch.internal",
            "placeholder",
            "free",
            "family_member");
        var activated = User.Restore(
            user.Id,
            user.FullName,
            "new@example.test",
            "bcrypt-hash",
            user.PlanId,
            false,
            null,
            null,
            70,
            true,
            false,
            0,
            null,
            null,
            user.Version + 1,
            user.SecurityVersion + 1,
            user.Role);
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        users.TryActivateCaregiverAsync(
                user.Id,
                user.Version,
                user.Email,
                "new@example.test",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(activated);

        var handler = CreateHandler(users, user.Id);
        var result = await handler.Handle(
            new ActivateCaregiverCommand("new@example.test", "Password1"),
            CancellationToken.None);

        result.User.Id.Should().Be(user.Id.ToString());
        result.User.Email.Should().Be("new@example.test");
        await users.Received(1).TryActivateCaregiverAsync(
            user.Id,
            user.Version,
            user.Email,
            "new@example.test",
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private static ActivateCaregiverCommandHandler CreateHandler(IUserRepository users, Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(userId);
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Hash(Arg.Any<string>()).Returns("bcrypt-hash");
        var jwt = Substitute.For<IJwtTokenService>();
        jwt.Create(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>())
            .Returns(new JwtToken("jwt", DateTimeOffset.UtcNow.AddDays(7), "jti"));
        return new ActivateCaregiverCommandHandler(currentUser, users, hasher, jwt);
    }
}
