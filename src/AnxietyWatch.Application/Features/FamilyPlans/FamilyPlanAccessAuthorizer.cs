using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Domain.FamilyPlans;
using AnxietyWatch.Domain.Tokens;
using AnxietyWatch.Domain.Users;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Application.Features.FamilyPlans;

public interface IFamilyPlanPatientAuthorizer
{
    Task<bool> CanManagePatientAsync(Guid ownerUserId, Guid patientUserId, CancellationToken cancellationToken = default);
}

public sealed class FamilyPlanPatientAuthorizer(IUserRepository users, IFamilyPlanPatientMembershipRepository memberships) : IFamilyPlanPatientAuthorizer
{
    public async Task<bool> CanManagePatientAsync(Guid ownerUserId, Guid patientUserId, CancellationToken cancellationToken = default)
    {
        var owner = await users.GetByIdAsync(ownerUserId, cancellationToken);
        return owner is not null && string.Equals(owner.PlanId, "family", StringComparison.OrdinalIgnoreCase) && await memberships.CanManagePatientAsync(ownerUserId, patientUserId, cancellationToken);
    }
}

public sealed class FamilyPlanPatientMembershipReconciler(ILinkTokenRepository tokens, IUserRepository users, IFamilyPlanPatientMembershipRepository memberships, ISystemClock clock, ILogger<FamilyPlanPatientMembershipReconciler> logger)
{
    public async Task<int> ReconcileAcceptedPatientTokensAsync(CancellationToken cancellationToken = default)
    {
        var count = 0;
        foreach (var token in await tokens.GetAcceptedPatientTokensAsync(cancellationToken))
        {
            try
            {
                if (!token.AcceptedBy.HasValue) continue;
                var owner = await users.GetByIdAsync(token.UserId, cancellationToken);
                if (owner is null || !string.Equals(owner.PlanId, "family", StringComparison.OrdinalIgnoreCase)) continue;
                var patient = await users.GetByIdAsync(token.AcceptedBy.Value, cancellationToken);
                if (patient is null) continue;
                await memberships.EnsureMembershipAsync(owner.Id, patient.Id, token.Id, token.AcceptedAt ?? clock.UtcNow, cancellationToken);
                count++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Family plan patient membership reconciliation skipped one invalid record.");
            }
        }
        return count;
    }
}
