namespace AnxietyWatch.Domain.Caregivers;

public enum CaregiverInvitationStatus { Pending, Accepted, Deleted }

public sealed class CaregiverInvitation
{
    public CaregiverInvitation(Guid id, Guid issuedByUserId, Guid targetPatientId, string code, DateTimeOffset expiresAt)
    { Id = id; IssuedByUserId = issuedByUserId; TargetPatientId = targetPatientId; Code = code; ExpiresAt = expiresAt; }
    public Guid Id { get; }
    public Guid IssuedByUserId { get; }
    public Guid TargetPatientId { get; }
    public string Code { get; }
    public DateTimeOffset ExpiresAt { get; }
    public CaregiverInvitationStatus Status { get; private set; } = CaregiverInvitationStatus.Pending;
    public Guid? AcceptedByCaregiverId { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public void Accept(Guid caregiverId, DateTimeOffset acceptedAt) { Status = CaregiverInvitationStatus.Accepted; AcceptedByCaregiverId = caregiverId; AcceptedAt = acceptedAt; }
    public void Delete() => Status = CaregiverInvitationStatus.Deleted;
    public static CaregiverInvitation Restore(Guid id, Guid issuer, Guid patient, string code, DateTimeOffset expiresAt, CaregiverInvitationStatus status, Guid? acceptedBy, DateTimeOffset? acceptedAt) => new(id, issuer, patient, code, expiresAt) { Status = status, AcceptedByCaregiverId = acceptedBy, AcceptedAt = acceptedAt };
}
