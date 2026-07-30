using System.Collections.Concurrent;
using AnxietyWatch.Domain.Users;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<Guid, User> users = new();

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(users.TryGetValue(id, out var user) ? user : null);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        Task.FromResult(users.Values.FirstOrDefault(user =>
            string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)));

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        if (!users.TryAdd(user.Id, user))
        {
            throw new InvalidOperationException("The user already exists.");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        users[user.Id] = user;
        return Task.CompletedTask;
    }
}
