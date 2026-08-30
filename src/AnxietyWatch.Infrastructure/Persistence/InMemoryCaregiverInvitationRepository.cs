using System.Collections.Concurrent;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Caregivers;

namespace AnxietyWatch.Infrastructure.Persistence;

public sealed class InMemoryCaregiverInvitationRepository : ICaregiverInvitationRepository
{
    private readonly ConcurrentDictionary<Guid, CaregiverInvitation> invitations = new();
    public Task AddAsync(CaregiverInvitation invitation, CancellationToken cancellationToken = default)
    {
        if (invitations.Values.Any(x => string.Equals(x.Code, invitation.Code, StringComparison.OrdinalIgnoreCase))) throw new ConflictException("The invitation code already exists.");
        if (!invitations.TryAdd(invitation.Id, invitation)) throw new ConflictException("The invitation already exists.");
        return Task.CompletedTask;
    }
    public Task<CaregiverInvitation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => Task.FromResult(invitations.Values.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)));
    public Task<CaregiverInvitation?> TryAcceptAsync(Guid id, Guid caregiverId, DateTimeOffset acceptedAt, CancellationToken cancellationToken = default)
    {
        if (!invitations.TryGetValue(id, out var invitation) || invitation.Status != CaregiverInvitationStatus.Pending) return Task.FromResult<CaregiverInvitation?>(null);
        lock (invitation)
        {
            if (invitation.Status != CaregiverInvitationStatus.Pending) return Task.FromResult<CaregiverInvitation?>(null);
            invitation.Accept(caregiverId, acceptedAt);
            return Task.FromResult<CaregiverInvitation?>(invitation);
        }
    }
    public Task<bool> TryDeleteAsync(Guid id, Guid issuerId, CancellationToken cancellationToken = default)
    { if (!invitations.TryGetValue(id, out var invitation) || invitation.IssuedByUserId != issuerId || invitation.Status == CaregiverInvitationStatus.Accepted) return Task.FromResult(false); invitation.Delete(); return Task.FromResult(true); }
}
