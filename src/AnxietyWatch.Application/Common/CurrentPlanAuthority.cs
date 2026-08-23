using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Domain.Users;

namespace AnxietyWatch.Application.Common;

internal static class CurrentPlanAuthority
{
    public static async Task<string> RequirePlanIdAsync(
        ICurrentUser currentUser,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        var user = await users.GetByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new UnauthorizedApplicationException("The session is invalid.");

        return user.PlanId;
    }

    public static int TokenLimit(string planId) => planId.ToLowerInvariant() switch
    {
        "free" or "individual" => 1,
        "family" => 5,
        "professional" => 20,
        _ => 0
    };

    public static int? WeeklyEpisodeLimit(string planId) =>
        string.Equals(planId, "free", StringComparison.OrdinalIgnoreCase) ? 5 : null;

    public static bool AllowsPrivateMode(string planId) => planId.ToLowerInvariant() is "individual" or "family" or "professional";
}
