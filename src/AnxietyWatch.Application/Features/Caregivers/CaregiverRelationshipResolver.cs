using AnxietyWatch.Domain.Caregivers;
using AnxietyWatch.Domain.Tokens;

namespace AnxietyWatch.Application.Features.Caregivers;

public interface ICaregiverRelationshipResolver
{
    Task<bool> IsLinkedAsync(Guid caregiverId, Guid patientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListCaregiverIdsAsync(Guid patientId, CancellationToken cancellationToken = default);
}

public sealed class CaregiverRelationshipResolver(
    ILinkTokenRepository tokens,
    ICaregiverPatientLinkRepository links) : ICaregiverRelationshipResolver
{
    public async Task<bool> IsLinkedAsync(Guid caregiverId, Guid patientId, CancellationToken cancellationToken = default) =>
        await links.IsLinkedAsync(caregiverId, patientId, cancellationToken) ||
        await tokens.HasAcceptedCaregiverRelationshipAsync(patientId, caregiverId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListCaregiverIdsAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var explicitIds = (await links.ListByPatientAsync(patientId, cancellationToken))
            .Select(link => link.CaregiverId);
        var legacyIds = (await tokens.GetAsync(patientId, cancellationToken))
            .Where(token => token.Status == TokenStatus.Accepted &&
                           token.AcceptedBy.HasValue &&
                           string.Equals(token.Role, "family_member", StringComparison.Ordinal))
            .Select(token => token.AcceptedBy!.Value);
        return explicitIds.Concat(legacyIds).Distinct().ToArray();
    }
}
