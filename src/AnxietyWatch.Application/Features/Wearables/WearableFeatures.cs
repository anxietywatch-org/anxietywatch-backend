using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using FluentValidation;
using MediatR;
using AnxietyWatch.Domain.Notifications;

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

public sealed record SosCancelRequest(
    Guid EventId,
    Guid DeviceId,
    Guid? UserId,
    DateTimeOffset CancelledAt,
    string? Reason);

public sealed record SuspectedEventFeaturesRequest(
    double? HeartRateMean,
    double? HeartRateMax,
    double? HeartRateSlopeBpmPerMinute,
    double? HeartRateDeltaFromBaseline,
    double? RmssdMillis,
    double? SdnnMillis,
    double? MovementMagnitudeMean,
    double? MovementVariance,
    double ValidSampleRatio,
    long LastSampleAgeSeconds,
    int SampleCount);

public sealed record SuspectedEventBaselineRequest(
    long SampleCount,
    double MeanHeartRate,
    double HeartRateM2,
    long UpdatedAtEpochMillis);

public sealed record SuspectedEventRequest(
    Guid EventId,
    Guid DeviceId,
    Guid? UserId,
    Guid SessionId,
    long Sequence,
    DateTimeOffset DetectedAt,
    string State,
    double Score,
    string RulesVersion,
    SuspectedEventFeaturesRequest Features,
    SuspectedEventBaselineRequest Baseline);

public sealed record EventDecisionRequest(
    Guid EventId,
    Guid DeviceId,
    Guid? UserId,
    Guid SessionId,
    long Sequence,
    DateTimeOffset DetectedAt,
    DateTimeOffset RespondedAt,
    string Response);

public sealed record SubmissionResponse(Guid Id, bool Accepted, bool Duplicate);

public interface IWearableSyncRepository
{
    Task<bool> TryStoreTelemetryAsync(Guid userId, TelemetryBatchRequest batch, CancellationToken cancellationToken = default);
    Task<bool> TryStoreSosAsync(Guid userId, SosTriggerRequest trigger, CancellationToken cancellationToken = default);
    Task<bool> TryStoreSosCancellationAsync(Guid userId, SosCancelRequest cancellation, CancellationToken cancellationToken = default);
    Task<bool> TryStoreSuspectedEventAsync(Guid userId, SuspectedEventRequest suspectedEvent, CancellationToken cancellationToken = default);
    Task<bool> TryStoreEventDecisionAsync(Guid userId, EventDecisionRequest decision, CancellationToken cancellationToken = default);
    Task<TelemetryWindowResult> GetTelemetryWindowAsync(
        Guid userId,
        Guid deviceId,
        Guid sessionId,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken = default);
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
    IWearableSyncRepository repository,
    ICaregiverNotificationOutbox notificationOutbox)
    : IRequestHandler<TriggerSosCommand, SubmissionResponse>
{
    public async Task<SubmissionResponse> Handle(TriggerSosCommand command, CancellationToken cancellationToken)
    {
        var userId = SubmitTelemetryBatchCommandHandler.RequireMatchingUser(currentUser, command.Trigger.UserId);
        var accepted = await repository.TryStoreSosAsync(userId, command.Trigger, cancellationToken);
        await notificationOutbox.EnsureNotificationJobsAsync(
            userId, command.Trigger.EventId, CaregiverNotificationType.Sos, cancellationToken);

        return new SubmissionResponse(command.Trigger.EventId, accepted, !accepted);
    }
}

public sealed record CancelSosCommand(SosCancelRequest Cancellation) : IRequest<SubmissionResponse>;

public sealed class CancelSosCommandValidator : AbstractValidator<CancelSosCommand>
{
    public CancelSosCommandValidator()
    {
        RuleFor(command => command.Cancellation.EventId).NotEmpty();
        RuleFor(command => command.Cancellation.DeviceId).NotEmpty();
        RuleFor(command => command.Cancellation.CancelledAt).NotEmpty();
        RuleFor(command => command.Cancellation.Reason).MaximumLength(500);
    }
}

public sealed class CancelSosCommandHandler(
    ICurrentUser currentUser,
    IWearableSyncRepository repository)
    : IRequestHandler<CancelSosCommand, SubmissionResponse>
{
    public async Task<SubmissionResponse> Handle(CancelSosCommand command, CancellationToken cancellationToken)
    {
        var userId = SubmitTelemetryBatchCommandHandler.RequireMatchingUser(
            currentUser,
            command.Cancellation.UserId);
        var accepted = await repository.TryStoreSosCancellationAsync(
            userId,
            command.Cancellation,
            cancellationToken);
        return new SubmissionResponse(command.Cancellation.EventId, accepted, !accepted);
    }
}

public sealed record SubmitSuspectedEventCommand(SuspectedEventRequest SuspectedEvent) : IRequest<SubmissionResponse>;

public sealed class SuspectedEventFeaturesRequestValidator : AbstractValidator<SuspectedEventFeaturesRequest>
{
    public SuspectedEventFeaturesRequestValidator()
    {
        RuleFor(features => features.ValidSampleRatio).InclusiveBetween(0, 1);
        RuleFor(features => features.LastSampleAgeSeconds).GreaterThanOrEqualTo(0);
        RuleFor(features => features.SampleCount).GreaterThanOrEqualTo(0);
        RuleFor(features => features.RmssdMillis).GreaterThanOrEqualTo(0)
            .When(features => features.RmssdMillis.HasValue);
        RuleFor(features => features.SdnnMillis).GreaterThanOrEqualTo(0)
            .When(features => features.SdnnMillis.HasValue);
    }
}

public sealed class SuspectedEventBaselineRequestValidator : AbstractValidator<SuspectedEventBaselineRequest>
{
    public SuspectedEventBaselineRequestValidator()
    {
        RuleFor(baseline => baseline.SampleCount).GreaterThanOrEqualTo(0);
        RuleFor(baseline => baseline.MeanHeartRate).GreaterThanOrEqualTo(0);
        RuleFor(baseline => baseline.HeartRateM2).GreaterThanOrEqualTo(0);
        RuleFor(baseline => baseline.UpdatedAtEpochMillis).GreaterThanOrEqualTo(0);
    }
}

public sealed class SubmitSuspectedEventCommandValidator : AbstractValidator<SubmitSuspectedEventCommand>
{
    public SubmitSuspectedEventCommandValidator()
    {
        RuleFor(command => command.SuspectedEvent.EventId).NotEmpty();
        RuleFor(command => command.SuspectedEvent.DeviceId).NotEmpty();
        RuleFor(command => command.SuspectedEvent.SessionId).NotEmpty();
        RuleFor(command => command.SuspectedEvent.Sequence).GreaterThanOrEqualTo(0);
        RuleFor(command => command.SuspectedEvent.DetectedAt).NotEmpty();
        RuleFor(command => command.SuspectedEvent.State).NotEmpty().MaximumLength(64);
        RuleFor(command => command.SuspectedEvent.Score).InclusiveBetween(0, 1);
        RuleFor(command => command.SuspectedEvent.RulesVersion).NotEmpty().MaximumLength(64);
        RuleFor(command => command.SuspectedEvent.Features).NotNull();
        When(command => command.SuspectedEvent.Features is not null, () =>
        {
            RuleFor(command => command.SuspectedEvent.Features)
                .SetValidator(new SuspectedEventFeaturesRequestValidator());
        });
        RuleFor(command => command.SuspectedEvent.Baseline).NotNull();
        When(command => command.SuspectedEvent.Baseline is not null, () =>
        {
            RuleFor(command => command.SuspectedEvent.Baseline)
                .SetValidator(new SuspectedEventBaselineRequestValidator());
        });
    }
}

public sealed class SubmitSuspectedEventCommandHandler(
    ICurrentUser currentUser,
    IWearableSyncRepository repository,
    ISuspectedEventInferenceService inferenceService)
    : IRequestHandler<SubmitSuspectedEventCommand, SubmissionResponse>
{
    public async Task<SubmissionResponse> Handle(SubmitSuspectedEventCommand command, CancellationToken cancellationToken)
    {
        var userId = SubmitTelemetryBatchCommandHandler.RequireMatchingUser(
            currentUser,
            command.SuspectedEvent.UserId);
        var accepted = await repository.TryStoreSuspectedEventAsync(
            userId,
            command.SuspectedEvent,
            cancellationToken);
        if (accepted)
        {
            await inferenceService.RunInferenceAsync(userId, command.SuspectedEvent, cancellationToken);
        }

        return new SubmissionResponse(command.SuspectedEvent.EventId, accepted, !accepted);
    }
}

public sealed record SubmitEventDecisionCommand(EventDecisionRequest Decision) : IRequest<SubmissionResponse>;

public sealed class SubmitEventDecisionCommandValidator : AbstractValidator<SubmitEventDecisionCommand>
{
    private static readonly string[] PrimaryResponses = ["ACTIVITY_CONFIRMED", "USER_OK", "SUPPORT_REQUESTED"];

    public SubmitEventDecisionCommandValidator()
    {
        RuleFor(command => command.Decision.EventId).NotEmpty();
        RuleFor(command => command.Decision.DeviceId).NotEmpty();
        RuleFor(command => command.Decision.SessionId).NotEmpty();
        RuleFor(command => command.Decision.Sequence).GreaterThanOrEqualTo(0);
        RuleFor(command => command.Decision.DetectedAt).NotEmpty();
        RuleFor(command => command.Decision.RespondedAt).NotEmpty();
        RuleFor(command => command.Decision.Response)
            .Must(response => PrimaryResponses.Contains(response, StringComparer.OrdinalIgnoreCase));
        RuleFor(command => command.Decision)
            .Must(decision => decision.RespondedAt >= decision.DetectedAt)
            .WithMessage("respondedAt must be greater than or equal to detectedAt.");
    }
}

public sealed class SubmitEventDecisionCommandHandler(
    ICurrentUser currentUser,
    IWearableSyncRepository repository,
    ICaregiverNotificationOutbox notificationOutbox)
    : IRequestHandler<SubmitEventDecisionCommand, SubmissionResponse>
{
    public async Task<SubmissionResponse> Handle(SubmitEventDecisionCommand command, CancellationToken cancellationToken)
    {
        var userId = SubmitTelemetryBatchCommandHandler.RequireMatchingUser(
            currentUser,
            command.Decision.UserId);
        var accepted = await repository.TryStoreEventDecisionAsync(
            userId,
            command.Decision,
            cancellationToken);
        if (string.Equals(command.Decision.Response, "SUPPORT_REQUESTED", StringComparison.OrdinalIgnoreCase))
        {
            await notificationOutbox.EnsureNotificationJobsAsync(
                userId, command.Decision.EventId, CaregiverNotificationType.SupportRequested, cancellationToken);
        }
        return new SubmissionResponse(command.Decision.EventId, accepted, !accepted);
    }
}
