using System.Collections.Concurrent;
using AnxietyWatch.Domain.FamilyPlans;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryFamilyPlanPatientMembershipRepository : IFamilyPlanPatientMembershipRepository
{
    private readonly ConcurrentDictionary<(Guid Owner, Guid Patient), FamilyPlanPatientMembership> memberships = new();
    public Task<FamilyPlanPatientMembership> EnsureMembershipAsync(Guid ownerUserId, Guid patientUserId, Guid? sourceTokenId, DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var membership = memberships.GetOrAdd((ownerUserId, patientUserId), _ => new FamilyPlanPatientMembership(Guid.NewGuid(), ownerUserId, patientUserId, createdAt, sourceTokenId));
        return Task.FromResult(membership);
    }
    public Task<bool> CanManagePatientAsync(Guid ownerUserId, Guid patientUserId, CancellationToken cancellationToken = default) => Task.FromResult(memberships.TryGetValue((ownerUserId, patientUserId), out var membership) && membership.Status == FamilyPlanPatientMembershipStatus.Active);
    public Task<IReadOnlyList<FamilyPlanPatientMembership>> ListPatientsAsync(Guid ownerUserId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FamilyPlanPatientMembership>>(memberships.Values.Where(x => x.OwnerUserId == ownerUserId && x.Status == FamilyPlanPatientMembershipStatus.Active).OrderByDescending(x => x.CreatedAt).ToArray());
}
