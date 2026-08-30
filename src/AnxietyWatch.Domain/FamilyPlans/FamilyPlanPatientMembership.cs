namespace AnxietyWatch.Domain.FamilyPlans;

public enum FamilyPlanPatientMembershipStatus { Active }

public sealed class FamilyPlanPatientMembership
{
    public FamilyPlanPatientMembership(Guid id, Guid ownerUserId, Guid patientUserId, DateTimeOffset createdAt, Guid? sourceTokenId, FamilyPlanPatientMembershipStatus status = FamilyPlanPatientMembershipStatus.Active)
    {
        Id = id; OwnerUserId = ownerUserId; PatientUserId = patientUserId; CreatedAt = createdAt; SourceTokenId = sourceTokenId; Status = status;
    }
    public Guid Id { get; }
    public Guid OwnerUserId { get; }
    public Guid PatientUserId { get; }
    public DateTimeOffset CreatedAt { get; }
    public Guid? SourceTokenId { get; }
    public FamilyPlanPatientMembershipStatus Status { get; }
}
