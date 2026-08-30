using AnxietyWatch.Application.Features.FamilyPlans;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.FamilyPlans;

public sealed class FamilyPlanPatientMembershipReconciliationService(
    FamilyPlanPatientMembershipReconciler reconciler,
    ILogger<FamilyPlanPatientMembershipReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var reconciled = await reconciler.ReconcileAcceptedPatientTokensAsync(stoppingToken);
            logger.LogInformation("Family plan patient membership reconciliation completed: {ReconciledCount} records.", reconciled);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Family plan patient membership reconciliation failed.");
        }
    }
}
