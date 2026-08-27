using AnxietyWatch.Domain.Caregivers;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryCaregiverRelationshipAuditRepository : ICaregiverRelationshipAuditRepository
{
    private readonly object gate = new();
    private readonly List<CaregiverRelationshipAuditEvent> events = [];

    public Task AppendAsync(CaregiverRelationshipAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            events.Add(auditEvent);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CaregiverRelationshipAuditEvent>> GetAsync(
        Guid? patientId = null,
        Guid? caregiverId = null,
        CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            IReadOnlyList<CaregiverRelationshipAuditEvent> result = events
                .Where(item => (!patientId.HasValue || item.PatientId == patientId.Value) &&
                              (!caregiverId.HasValue || item.CaregiverId == caregiverId.Value))
                .OrderBy(item => item.OccurredAt)
                .ThenBy(item => item.AuditId)
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
