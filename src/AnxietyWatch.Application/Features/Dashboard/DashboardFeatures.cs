using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Episodes;
using AnxietyWatch.Domain.Users;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Dashboard;

public sealed record AnxietyLevelResponse(int Current, string Trend);

public sealed record WeeklyRecordsResponse(int Used, int? Limit);

public sealed record DashboardSummaryResponse(
    AnxietyLevelResponse AnxietyLevel,
    WeeklyRecordsResponse WeeklyRecords,
    int StreakDays,
    int ExercisesCompleted);

public sealed record GetDashboardSummaryQuery : IRequest<DashboardSummaryResponse>;

public sealed class GetDashboardSummaryQueryHandler(
    ICurrentUser currentUser,
    IEpisodeRepository episodes,
    IUserRepository users,
    ISystemClock clock)
    : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryResponse>
{
    public async Task<DashboardSummaryResponse> Handle(
        GetDashboardSummaryQuery request,
        CancellationToken cancellationToken)
    {
        RequireAuthenticatedUser(currentUser);
        var now = clock.UtcNow;
        var records = await episodes.GetAsync(currentUser.UserId, now.AddDays(-90), cancellationToken);
        var recent = records.OrderByDescending(item => item.Date).ToArray();
        var current = recent.FirstOrDefault()?.Intensity ?? 0;
        var previous = recent.Skip(1).FirstOrDefault()?.Intensity ?? current;
        var weekStart = StartOfWeek(now);
        var weeklyUsed = recent.Count(item => item.Date >= weekStart);
        var planId = await CurrentPlanAuthority.RequirePlanIdAsync(currentUser, users, cancellationToken);

        return new DashboardSummaryResponse(
            new AnxietyLevelResponse(current, current > previous ? "up" : current < previous ? "down" : "stable"),
            new WeeklyRecordsResponse(weeklyUsed, CurrentPlanAuthority.WeeklyEpisodeLimit(planId)),
            CalculateStreak(recent, now),
            0);
    }

    internal static void RequireAuthenticatedUser(ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }
    }

    internal static DateTimeOffset StartOfWeek(DateTimeOffset date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, date.Offset)
            .AddDays(-daysSinceMonday);
    }

    private static int CalculateStreak(IReadOnlyCollection<Episode> episodes, DateTimeOffset now)
    {
        var dates = episodes.Select(item => item.Date.Date).ToHashSet();
        var streak = 0;
        var date = now.Date;
        while (dates.Contains(date))
        {
            streak++;
            date = date.AddDays(-1);
        }

        return streak;
    }
}

public sealed record EpisodeResponse(
    string Id,
    DateTimeOffset Date,
    int Intensity,
    IReadOnlyCollection<string> Symptoms,
    string? Notes);

public sealed record GetEpisodesQuery(int Range) : IRequest<IReadOnlyList<EpisodeResponse>>;

public sealed class GetEpisodesQueryValidator : AbstractValidator<GetEpisodesQuery>
{
    public GetEpisodesQueryValidator() => RuleFor(query => query.Range).Must(value => value is 7 or 30 or 90);
}

public sealed class GetEpisodesQueryHandler(ICurrentUser currentUser, IEpisodeRepository episodes, ISystemClock clock)
    : IRequestHandler<GetEpisodesQuery, IReadOnlyList<EpisodeResponse>>
{
    public async Task<IReadOnlyList<EpisodeResponse>> Handle(
        GetEpisodesQuery request,
        CancellationToken cancellationToken)
    {
        GetDashboardSummaryQueryHandler.RequireAuthenticatedUser(currentUser);
        var entities = await episodes.GetAsync(currentUser.UserId, clock.UtcNow.AddDays(-request.Range), cancellationToken);
        return entities.Select(Map).ToArray();
    }

    internal static EpisodeResponse Map(Episode episode) =>
        new(episode.Id.ToString(), episode.Date, episode.Intensity, episode.Symptoms, episode.Notes);
}

public sealed record CreateEpisodeCommand(
    int Intensity,
    IReadOnlyCollection<string> Symptoms,
    string? Notes) : IRequest<EpisodeResponse>;

public sealed class CreateEpisodeCommandValidator : AbstractValidator<CreateEpisodeCommand>
{
    public CreateEpisodeCommandValidator()
    {
        RuleFor(command => command.Intensity).InclusiveBetween(0, 100);
        RuleFor(command => command.Notes).MaximumLength(500);
    }
}

public sealed class CreateEpisodeCommandHandler(
    ICurrentUser currentUser,
    IEpisodeRepository episodes,
    IUserRepository users,
    ISystemClock clock)
    : IRequestHandler<CreateEpisodeCommand, EpisodeResponse>
{
    public async Task<EpisodeResponse> Handle(CreateEpisodeCommand command, CancellationToken cancellationToken)
    {
        GetDashboardSummaryQueryHandler.RequireAuthenticatedUser(currentUser);
        var now = clock.UtcNow;
        var planId = await CurrentPlanAuthority.RequirePlanIdAsync(currentUser, users, cancellationToken);
        if (CurrentPlanAuthority.WeeklyEpisodeLimit(planId) is 5 &&
            await episodes.CountAsync(currentUser.UserId, GetDashboardSummaryQueryHandler.StartOfWeek(now), cancellationToken) >= 5)
        {
            throw new ForbiddenException("The weekly episode quota for the free plan has been reached.");
        }

        var episode = new Episode(Guid.NewGuid(), currentUser.UserId, now, command.Intensity,
            command.Symptoms ?? [], command.Notes);
        await episodes.AddAsync(episode, cancellationToken);
        return GetEpisodesQueryHandler.Map(episode);
    }
}
