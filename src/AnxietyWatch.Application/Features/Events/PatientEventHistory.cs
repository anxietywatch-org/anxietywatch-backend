using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Events;

public sealed record PatientEventResponse(
    Guid EventId,
    string Type,
    DateTimeOffset OccurredAt,
    string? Status);

public sealed record GetPatientEventHistoryQuery(int Limit)
    : IRequest<IReadOnlyList<PatientEventResponse>>;

public sealed class GetPatientEventHistoryQueryValidator
    : AbstractValidator<GetPatientEventHistoryQuery>
{
    public GetPatientEventHistoryQueryValidator() =>
        RuleFor(query => query.Limit).InclusiveBetween(1, 100);
}

public sealed class GetPatientEventHistoryQueryHandler(
    ICurrentUser currentUser,
    IPatientEventRepository events)
    : IRequestHandler<GetPatientEventHistoryQuery, IReadOnlyList<PatientEventResponse>>
{
    public async Task<IReadOnlyList<PatientEventResponse>> Handle(
        GetPatientEventHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        var records = await events.GetAsync(currentUser.UserId, request.Limit, cancellationToken);
        return records.Select(record => new PatientEventResponse(
            record.EventId,
            record.Type,
            record.OccurredAt,
            record.Status)).ToArray();
    }
}
