using System.Collections.Concurrent;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Tokens;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryLinkTokenRepository : ILinkTokenRepository
{
    private readonly ConcurrentDictionary<Guid, LinkToken> tokens = new();
    private readonly object gate = new();

    public Task<IReadOnlyList<LinkToken>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<LinkToken> result = tokens.Values
                .Where(token => token.UserId == userId && token.Status != TokenStatus.Deleted)
                .OrderByDescending(token => token.ExpiresAt)
                .Select(Clone)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<bool> TryAddAsync(LinkToken token, int maximum, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var activeCount = tokens.Values.Count(existing =>
                existing.UserId == token.UserId && existing.Status != TokenStatus.Deleted);
            if (activeCount >= maximum ||
                tokens.Values.Any(existing => existing.Code == token.Code) ||
                !tokens.TryAdd(token.Id, Clone(token)))
            {
                return Task.FromResult(false);
            }

            return Task.FromResult(true);
        }
    }

    public Task<LinkToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(tokens.TryGetValue(id, out var token) ? Clone(token) : null);
        }
    }

    public Task UpdateAsync(LinkToken token, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!tokens.TryGetValue(token.Id, out var current) ||
                token.Status == TokenStatus.Accepted &&
                (current.Status != TokenStatus.Pending || token.AcceptedAt >= token.ExpiresAt) ||
                token.Status == TokenStatus.Deleted && current.Status == TokenStatus.Accepted)
            {
                throw new ConflictException("The token state changed before the request completed.");
            }

            tokens[token.Id] = Clone(token);
            return Task.CompletedTask;
        }
    }

    private static LinkToken Clone(LinkToken token) => LinkToken.Restore(
        token.Id,
        token.UserId,
        token.Code,
        token.Role,
        token.ExpiresAt,
        token.Status,
        token.AcceptedBy,
        token.AcceptedAt);
}
