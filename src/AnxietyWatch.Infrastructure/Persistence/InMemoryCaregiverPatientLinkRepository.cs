using System.Collections.Concurrent;
using AnxietyWatch.Domain.Caregivers;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryCaregiverPatientLinkRepository : ICaregiverPatientLinkRepository
{
    private readonly ConcurrentDictionary<(Guid Caregiver, Guid Patient), CaregiverPatientLink> links = new();
    public Task<CaregiverPatientLink> EnsureLinkAsync(Guid caregiverId, Guid patientId, Guid? sourceInvitationId, DateTimeOffset createdAt, CancellationToken cancellationToken = default) => Task.FromResult(links.GetOrAdd((caregiverId, patientId), _ => new CaregiverPatientLink(Guid.NewGuid(), caregiverId, patientId, createdAt, sourceInvitationId)));
    public Task<bool> IsLinkedAsync(Guid caregiverId, Guid patientId, CancellationToken cancellationToken = default) => Task.FromResult(links.ContainsKey((caregiverId, patientId)));
    public Task<bool> RemoveLinkAsync(Guid caregiverId, Guid patientId, CancellationToken cancellationToken = default) => Task.FromResult(links.TryRemove((caregiverId, patientId), out _));
    public Task<IReadOnlyList<CaregiverPatientLink>> ListByCaregiverAsync(Guid caregiverId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CaregiverPatientLink>>(links.Values.Where(x => x.CaregiverId == caregiverId).OrderByDescending(x => x.CreatedAt).ToArray());
    public Task<IReadOnlyList<CaregiverPatientLink>> ListByPatientAsync(Guid patientId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CaregiverPatientLink>>(links.Values.Where(x => x.PatientId == patientId).OrderByDescending(x => x.CreatedAt).ToArray());
}
