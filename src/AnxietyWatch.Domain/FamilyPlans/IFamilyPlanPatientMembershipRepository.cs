namespace AnxietyWatch.Domain.FamilyPlans;

public interface IFamilyPlanPatientMembershipRepository
{
    Task<FamilyPlanPatientMembership> EnsureMembershipAsync(Guid ownerUserId, Guid patientUserId, Guid? sourceTokenId, DateTimeOffset createdAt, CancellationToken cancellationToken = default);
    Task<bool> CanManagePatientAsync(Guid ownerUserId, Guid patientUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FamilyPlanPatientMembership>> ListPatientsAsync(Guid ownerUserId, CancellationToken cancellationToken = default);
}
