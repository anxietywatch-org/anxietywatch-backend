using System.Collections.Concurrent;
using AnxietyWatch.Domain.Tokens;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryLinkTokenRepository : ILinkTokenRepository
{
    private readonly ConcurrentDictionary<Guid, LinkToken> tokens = new();
    private readonly object quotaLock = new();

    public Task<IReadOnlyList<LinkToken>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LinkToken> result = tokens.Values
            .Where(token => token.UserId == userId && token.Status != TokenStatus.Deleted)
            .OrderByDescending(token => token.ExpiresAt)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<bool> TryAddAsync(LinkToken token, int maximum, CancellationToken cancellationToken = default)
    {
        lock (quotaLock)
        {
            var activeCount = tokens.Values.Count(existing =>
                existing.UserId == token.UserId && existing.Status != TokenStatus.Deleted);
            if (activeCount >= maximum || !tokens.TryAdd(token.Id, token))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
    }

    public Task<LinkToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(tokens.TryGetValue(id, out var token) ? token : null);

    public Task UpdateAsync(LinkToken token, CancellationToken cancellationToken = default)
    {
        tokens[token.Id] = token;
        return Task.CompletedTask;
    }
}
