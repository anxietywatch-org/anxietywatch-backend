namespace AnxietyWatch.Domain.Tokens;

public sealed record AcceptedCaregiverRelationship(Guid PatientId, string Role, DateTimeOffset LinkedAt);

public interface ILinkTokenRepository
{
    Task<IReadOnlyList<LinkToken>> GetAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<bool> TryAddAsync(LinkToken token, int maximum, CancellationToken cancellationToken = default);
    Task<LinkToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<LinkToken?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LinkToken>> GetAcceptedPatientTokensAsync(CancellationToken cancellationToken = default);
    Task<bool> HasAcceptedCaregiverRelationshipAsync(
        Guid patientId,
        Guid caregiverId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AcceptedCaregiverRelationship>> GetAcceptedCaregiverRelationshipsAsync(
        Guid caregiverId,
        CancellationToken cancellationToken = default);
    Task<LinkToken?> TryRotateAsync(
        Guid id,
        Guid ownerId,
        string expectedCode,
        string newCode,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
    Task<bool> TryAcceptAsync(
        Guid id,
        string expectedCode,
        Guid acceptedBy,
        DateTimeOffset acceptedAt,
        CancellationToken cancellationToken = default);
    Task<bool> TryDeleteAsync(Guid id, string expectedCode, CancellationToken cancellationToken = default);
    Task<bool> TryRevokeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<long> RevokeAcceptedCaregiverRelationshipsAsync(Guid patientId, Guid caregiverId, CancellationToken cancellationToken = default);
    Task UpdateAsync(LinkToken token, CancellationToken cancellationToken = default);
}
