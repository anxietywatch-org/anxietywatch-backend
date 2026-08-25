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

    public Task<LinkToken?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var token = tokens.Values.FirstOrDefault(existing =>
                string.Equals(existing.Code, code, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(token is null ? null : Clone(token));
        }
    }

    public Task<bool> HasAcceptedCaregiverRelationshipAsync(
        Guid patientId,
        Guid caregiverId,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(tokens.Values.Any(token =>
                token.UserId == patientId &&
                token.AcceptedBy == caregiverId &&
                token.Status == TokenStatus.Accepted &&
                string.Equals(token.Role, "family_member", StringComparison.OrdinalIgnoreCase)));
        }
    }

    public Task<LinkToken?> TryRotateAsync(
        Guid id,
        Guid ownerId,
        string expectedCode,
        string newCode,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!tokens.TryGetValue(id, out var current) ||
                current.UserId != ownerId ||
                current.Status != TokenStatus.Pending ||
                !string.Equals(current.Code, expectedCode, StringComparison.Ordinal) ||
                tokens.Values.Any(existing => existing.Id != id && existing.Code == newCode))
            {
                return Task.FromResult<LinkToken?>(null);
            }

            var rotated = Clone(current);
            rotated.Rotate(newCode, expiresAt);
            tokens[id] = rotated;
            return Task.FromResult<LinkToken?>(Clone(rotated));
        }
    }

    public Task<bool> TryAcceptAsync(
        Guid id,
        string expectedCode,
        Guid acceptedBy,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!tokens.TryGetValue(id, out var current) ||
                current.Status != TokenStatus.Pending ||
                !string.Equals(current.Code, expectedCode, StringComparison.Ordinal) ||
                current.ExpiresAt <= acceptedAt)
            {
                return Task.FromResult(false);
            }

            tokens[id] = LinkToken.Restore(
                current.Id,
                current.UserId,
                current.Code,
                current.Role,
                current.ExpiresAt,
                TokenStatus.Accepted,
                acceptedBy,
                acceptedAt);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryDeleteAsync(Guid id, string expectedCode, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!tokens.TryGetValue(id, out var current) ||
                current.Status == TokenStatus.Accepted ||
                !string.Equals(current.Code, expectedCode, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            tokens[id] = LinkToken.Restore(
                current.Id,
                current.UserId,
                current.Code,
                current.Role,
                current.ExpiresAt,
                TokenStatus.Deleted,
                current.AcceptedBy,
                current.AcceptedAt);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TryRevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!tokens.TryGetValue(id, out var current) || current.Status != TokenStatus.Accepted)
            {
                return Task.FromResult(false);
            }

            tokens[id] = LinkToken.Restore(
                current.Id,
                current.UserId,
                current.Code,
                current.Role,
                current.ExpiresAt,
                TokenStatus.Deleted,
                current.AcceptedBy,
                current.AcceptedAt);
            return Task.FromResult(true);
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
