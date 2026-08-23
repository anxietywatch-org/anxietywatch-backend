namespace AnxietyWatch.Domain.Tokens;

public interface ILinkTokenRepository
{
    Task<IReadOnlyList<LinkToken>> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> TryAddAsync(LinkToken token, int maximum, CancellationToken cancellationToken = default);
    Task<LinkToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LinkToken?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<LinkToken?> TryRotateAsync(
        Guid id,
        Guid ownerId,
        string expectedCode,
        string newCode,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
    Task<bool> TryAcceptAsync(Guid id, Guid acceptedBy, DateTimeOffset acceptedAt, CancellationToken cancellationToken = default);
    Task<bool> TryRevokeAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpdateAsync(LinkToken token, CancellationToken cancellationToken = default);
}
