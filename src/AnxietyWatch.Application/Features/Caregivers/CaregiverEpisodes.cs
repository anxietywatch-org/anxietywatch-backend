using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Episodes;
using AnxietyWatch.Domain.Users;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Caregivers;

public sealed record CaregiverEpisodeResponse(
    DateTimeOffset Date,
    int Intensity,
    IReadOnlyCollection<string>? Symptoms,
    string? Notes,
    bool DetailsHidden);

public sealed record GetCaregiverPatientEpisodesQuery(Guid PatientId, int Range)
    : IRequest<IReadOnlyList<CaregiverEpisodeResponse>>;

public sealed class GetCaregiverPatientEpisodesQueryValidator
    : AbstractValidator<GetCaregiverPatientEpisodesQuery>
{
    public GetCaregiverPatientEpisodesQueryValidator() =>
        RuleFor(query => query.Range).Must(value => value is 7 or 30 or 90);
}

public sealed class GetCaregiverPatientEpisodesQueryHandler(
    ICaregiverAccessAuthorizer authorizer,
    IUserRepository users,
    IEpisodeRepository episodes,
    ISystemClock clock)
    : IRequestHandler<GetCaregiverPatientEpisodesQuery, IReadOnlyList<CaregiverEpisodeResponse>>
{
    public async Task<IReadOnlyList<CaregiverEpisodeResponse>> Handle(
        GetCaregiverPatientEpisodesQuery request,
        CancellationToken cancellationToken)
    {
        await authorizer.RequireCaregiverAccessAsync(request.PatientId, cancellationToken);

        var patient = await users.GetByIdAsync(request.PatientId, cancellationToken)
            ?? throw new NotFoundException("The patient was not found.");
        var from = clock.UtcNow.AddDays(-request.Range);
        var records = await episodes.GetAsync(patient.Id, from, cancellationToken);
        var detailsHidden = !patient.PrivateModeResolved || patient.PrivateMode;

        return records.Select(episode => new CaregiverEpisodeResponse(
            episode.Date,
            episode.Intensity,
            detailsHidden ? null : episode.Symptoms,
            detailsHidden ? null : episode.Notes,
            detailsHidden)).ToArray();
    }
}
