using AnxietyWatch.Domain.Users;
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
    }
}
