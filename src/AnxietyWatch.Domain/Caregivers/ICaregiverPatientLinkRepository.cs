namespace AnxietyWatch.Domain.Caregivers;

public interface ICaregiverPatientLinkRepository
{
    Task<CaregiverPatientLink> EnsureLinkAsync(Guid caregiverId, Guid patientId, Guid? sourceInvitationId, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
    Task<bool> IsLinkedAsync(Guid caregiverId, Guid patientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CaregiverPatientLink>> ListByCaregiverAsync(Guid caregiverId, CancellationToken cancellationToken = default);
}
