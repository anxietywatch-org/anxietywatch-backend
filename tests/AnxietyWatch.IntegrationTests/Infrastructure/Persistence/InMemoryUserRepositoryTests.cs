using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Security;
using FluentAssertions;

namespace AnxietyWatch.IntegrationTests.Infrastructure.Persistence;

public sealed class InMemoryUserRepositoryTests
{
    [Fact]
    public async Task PasswordChange_ShouldInvalidatePreviouslyReadLoginSnapshot()
    {
        var repository = new InMemoryUserRepository();
        var user = new User(Guid.NewGuid(), "Test User", "test@example.test", "old-hash", "free");
        await repository.AddAsync(user);
        var loginSnapshot = await repository.GetByEmailAsync(user.Email);

        (await repository.UpdatePasswordAsync(user.Id, "new-hash")).Should().BeTrue();

        var result = await repository.RegisterSuccessfulLoginAsync(
            user.Id,
            DateTimeOffset.UtcNow,
            loginSnapshot!.Version,
            loginSnapshot.PasswordHash);
        result.Should().BeNull();
        (await repository.GetByIdAsync(user.Id))!.PasswordHash.Should().Be("new-hash");
    }
}
