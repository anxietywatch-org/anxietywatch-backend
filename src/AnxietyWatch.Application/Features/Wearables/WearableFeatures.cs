using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using FluentValidation;
using MediatR;

namespace AnxietyWatch.Application.Features.Wearables;

public sealed record TelemetryQualityRequest(string HeartRate, string Ibi, string WearingState);

public sealed record AccelerometerRequest(double X, double Y, double Z);

public sealed record TelemetrySampleRequest(
    DateTimeOffset Timestamp,
    double? HeartRateBpm,
    IReadOnlyList<double> IbiMs,
    AccelerometerRequest? Accelerometer,
    double? SkinTemperatureCelsius,
    double? AmbientTemperatureCelsius,
    TelemetryQualityRequest Quality);

public sealed record TelemetryBatchRequest(
    Guid BatchId,
    Guid DeviceId,
    Guid? UserId,
    Guid SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    long Sequence,
    IReadOnlyList<TelemetrySampleRequest> Samples);

public sealed record SosTriggerRequest(
    Guid EventId,
    Guid DeviceId,
    Guid? UserId,
    DateTimeOffset TriggeredAt,
    string Source,
    string? Reason);

public sealed record SubmissionResponse(Guid Id, bool Accepted, bool Duplicate);

public interface IWearableSyncRepository
{
    Task<bool> TryStoreTelemetryAsync(Guid userId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default);
    Task<bool> TryStoreSosAsync(Guid userId, SosTriggerRequest trigger, CancellationToken cancellationToken = default);
}

public sealed record SubmitTelemetryBatchCommand(TelemetryBatchRequest Batch) : IRequest<SubmissionResponse>;

public sealed class SubmitTelemetryBatchCommandValidator : AbstractValidator<SubmitTelemetryBatchCommand>
{
    public SubmitTelemetryBatchCommandValidator()
    {
        RuleFor(command => command.Batch.BatchId).NotEmpty();
        RuleFor(command => command.Batch.DeviceId).NotEmpty();
        RuleFor(command => command.Batch.SessionId).NotEmpty();
        RuleFor(command => command.Batch.Sequence).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Batch.Samples).NotNull().Must(samples => samples is { Count: >= 1 and <= 600 });
        RuleFor(command => command.Batch).Must(batch => batch.EndedAt >= batch.StartedAt)
            .WithMessage("endedAt must be greater than or equal to startedAt.");
        RuleForEach(command => command.Batch.Samples).SetValidator(new TelemetrySampleRequestValidator());
    }
}

public sealed class TelemetrySampleRequestValidator : AbstractValidator<TelemetrySampleRequest>
{
    private static readonly string[] QualityValues = ["good", "fair", "poor", "unknown"];
    private static readonly string[] WearingStates = ["onBody", "offBody", "unknown"];

    public TelemetrySampleRequestValidator()
    {
        RuleFor(sample => sample.IbiMs).NotNull().Must(values => values.Count <= 16);
        RuleForEach(sample => sample.IbiMs).GreaterThan(0);
        RuleFor(sample => sample.HeartRateBpm).GreaterThan(0).When(sample => sample.HeartRateBpm.HasValue);
        RuleFor(sample => sample.Quality).NotNull();
        When(sample => sample.Quality is not null, () =>
        {
            RuleFor(sample => sample.Quality.HeartRate).Must(value => QualityValues.Contains(value, StringComparer.OrdinalIgnoreCase));
            RuleFor(sample => sample.Quality.Ibi).Must(value => QualityValues.Contains(value, StringComparer.OrdinalIgnoreCase));
            RuleFor(sample => sample.Quality.WearingState).Must(value => WearingStates.Contains(value, StringComparer.OrdinalIgnoreCase));
        });
    }
}

public sealed class SubmitTelemetryBatchCommandHandler(
    ICurrentUser currentUser,
    IWearableSyncRepository repository)
    : IRequestHandler<SubmitTelemetryBatchCommand, SubmissionResponse>
{
    public async Task<SubmissionResponse> Handle(SubmitTelemetryBatchCommand command, CancellationToken cancellationToken)
    {
        var userId = RequireMatchingUser(currentUser, command.Batch.UserId);
        var accepted = await repository.TryStoreTelemetryAsync(userId, command.Batch, cancellationToken);
        return new SubmissionResponse(command.Batch.BatchId, accepted, !accepted);
    }

    internal static Guid RequireMatchingUser(ICurrentUser currentUser, Guid? suppliedUserId)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new UnauthorizedApplicationException("Authentication is required.");
        }

        if (suppliedUserId.HasValue && suppliedUserId.Value != currentUser.UserId)
        {
            throw new ForbiddenException("The supplied userId does not match the authenticated user.");
        }

        return currentUser.UserId;
    }
}

public sealed record TriggerSosCommand(SosTriggerRequest Trigger) : IRequest<SubmissionResponse>;

public sealed class TriggerSosCommandValidator : AbstractValidator<TriggerSosCommand>
{
    public TriggerSosCommandValidator()
    {
        RuleFor(command => command.Trigger.EventId).NotEmpty();
        RuleFor(command => command.Trigger.DeviceId).NotEmpty();
        RuleFor(command => command.Trigger.Source)
            .Must(source => string.Equals(source, "WATCH", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(source, "MOBILE", StringComparison.OrdinalIgnoreCase));
        RuleFor(command => command.Trigger.Reason).MaximumLength(500);
    }
}

public sealed class TriggerSosCommandHandler(
    ICurrentUser currentUser,
    IWearableSyncRepository repository)
    : IRequestHandler<TriggerSosCommand, SubmissionResponse>
{
    public async Task<SubmissionResponse> Handle(TriggerSosCommand command, CancellationToken cancellationToken)
    {
        var userId = SubmitTelemetryBatchCommandHandler.RequireMatchingUser(currentUser, command.Trigger.UserId);
        var accepted = await repository.TryStoreSosAsync(userId, command.Trigger, cancellationToken);
        return new SubmissionResponse(command.Trigger.EventId, accepted, !accepted);
    }
}
