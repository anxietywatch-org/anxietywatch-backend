namespace AnxietyWatch.Domain.Caregivers;

public interface ICaregiverInvitationRepository
{
    Task AddAsync(CaregiverInvitation invitation, CancellationToken cancellationToken = default);
    Task<CaregiverInvitation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<CaregiverInvitation?> TryAcceptAsync(Guid id, Guid caregiverId, DateTimeOffset acceptedAt, CancellationToken cancellationToken = default);
    Task<bool> TryDeleteAsync(Guid id, Guid issuerId, CancellationToken cancellationToken = default);
}
