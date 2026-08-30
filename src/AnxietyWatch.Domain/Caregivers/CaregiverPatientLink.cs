namespace AnxietyWatch.Domain.Caregivers;

public sealed class CaregiverPatientLink
{
    public CaregiverPatientLink(Guid id, Guid caregiverId, Guid patientId, DateTimeOffset createdAt, Guid? sourceInvitationId)
    { Id = id; CaregiverId = caregiverId; PatientId = patientId; CreatedAt = createdAt; SourceInvitationId = sourceInvitationId; }
    public Guid Id { get; }
    public Guid CaregiverId { get; }
    public Guid PatientId { get; }
    public DateTimeOffset CreatedAt { get; }
    public Guid? SourceInvitationId { get; }
}
