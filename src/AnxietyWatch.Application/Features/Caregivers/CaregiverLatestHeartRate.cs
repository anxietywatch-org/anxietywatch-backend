using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using MediatR;

namespace AnxietyWatch.Application.Features.Caregivers;

public sealed record CaregiverLatestHeartRateResponse(
    double HeartRateBpm,
    DateTimeOffset MeasuredAt,
    long AgeSeconds,
    string? Quality);

public sealed record LatestHeartRateRecord(
    double HeartRateBpm,
    DateTimeOffset MeasuredAt,
    string? Quality);

public interface IPatientHeartRateRepository
{
    Task<LatestHeartRateRecord?> GetLatestAsync(
        Guid patientId,
        CancellationToken cancellationToken = default);
}

public sealed record GetCaregiverLatestHeartRateQuery(Guid PatientId)
    : IRequest<CaregiverLatestHeartRateResponse?>;

public sealed class GetCaregiverLatestHeartRateQueryHandler(
    ICaregiverAccessAuthorizer authorizer,
    IPatientHeartRateRepository heartRates,
    ISystemClock clock)
    : IRequestHandler<GetCaregiverLatestHeartRateQuery, CaregiverLatestHeartRateResponse?>
{
    public async Task<CaregiverLatestHeartRateResponse?> Handle(
        GetCaregiverLatestHeartRateQuery request,
        CancellationToken cancellationToken)
    {
        await authorizer.RequireCaregiverAccessAsync(request.PatientId, cancellationToken);
        var latest = await heartRates.GetLatestAsync(request.PatientId, cancellationToken);
        if (latest is null)
        {
            return null;
        }

        var ageSeconds = Math.Max(0, (long)(clock.UtcNow - latest.MeasuredAt).TotalSeconds);
        return new CaregiverLatestHeartRateResponse(
            latest.HeartRateBpm,
            latest.MeasuredAt,
            ageSeconds,
            latest.Quality);
    }
}
