using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Caregivers;

public sealed record CaregiverEventResponse(
    Guid EventId,
    string Type,
    DateTimeOffset OccurredAt,
    string? Status);

public sealed record GetCaregiverPatientEventsQuery(Guid PatientId, int Limit)
    : IRequest<IReadOnlyList<CaregiverEventResponse>>;

public sealed class GetCaregiverPatientEventsQueryValidator
    : AbstractValidator<GetCaregiverPatientEventsQuery>
{
    public GetCaregiverPatientEventsQueryValidator() =>
        RuleFor(query => query.Limit).InclusiveBetween(1, 100);
}

public sealed record PatientEventRecord(
    Guid PatientId,
    Guid EventId,
    string Type,
    DateTimeOffset OccurredAt,
    string? Status);

public interface IPatientEventRepository
{
    Task<IReadOnlyList<PatientEventRecord>> GetAsync(
        Guid patientId,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed class GetCaregiverPatientEventsQueryHandler(
    ICaregiverAccessAuthorizer authorizer,
    IPatientEventRepository events)
    : IRequestHandler<GetCaregiverPatientEventsQuery, IReadOnlyList<CaregiverEventResponse>>
{
    public async Task<IReadOnlyList<CaregiverEventResponse>> Handle(
        GetCaregiverPatientEventsQuery request,
        CancellationToken cancellationToken)
    {
        await authorizer.RequireCaregiverAccessAsync(request.PatientId, cancellationToken);
        var records = await events.GetAsync(request.PatientId, request.Limit, cancellationToken);

        return records.Select(record => new CaregiverEventResponse(
            record.EventId,
            record.Type,
            record.OccurredAt,
            record.Status)).ToArray();
    }
}
