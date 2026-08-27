namespace AnxietyWatch.Domain.Caregivers;

public enum CaregiverRelationshipAuditAction
{
    AcceptedInitial,
    AcceptedAdditional,
    Revoked
}

public sealed record CaregiverRelationshipAuditEvent(
    Guid AuditId,
    Guid PatientId,
    Guid CaregiverId,
    Guid SourceTokenId,
    CaregiverRelationshipAuditAction Action,
    DateTimeOffset OccurredAt);

public interface ICaregiverRelationshipAuditRepository
{
    Task AppendAsync(CaregiverRelationshipAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CaregiverRelationshipAuditEvent>> GetAsync(
        Guid? patientId = null,
        Guid? caregiverId = null,
        CancellationToken cancellationToken = default);
}
