using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Domain.Notifications;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;

namespace AnxietyWatch.Infrastructure.Notifications;

public sealed class FirebasePushNotificationSender : IPushNotificationSender
{
    private readonly FirebaseMessaging messaging;

    public FirebasePushNotificationSender(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Firebase:Enabled"))
            throw new InvalidOperationException("Firebase sender was created while Firebase:Enabled is false.");
        var path = configuration["Firebase:CredentialsPath"];
        var json = configuration["Firebase:CredentialsJson"];
        if (string.IsNullOrWhiteSpace(path) == string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Configure exactly one Firebase credential source.");
        var credential = !string.IsNullOrWhiteSpace(path)
            ? CredentialFactory.FromFile(path, "service_account")
            : CredentialFactory.FromJson(json!, "service_account");
        var app = FirebaseApp.Create(new AppOptions
        {
            Credential = credential,
            ProjectId = configuration["Firebase:ProjectId"]
        }, $"anxietywatch-{Guid.NewGuid():N}");
        messaging = FirebaseMessaging.GetMessaging(app);
    }

    public async Task<PushSendResult> SendAsync(string registrationToken, NotificationPayload payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await messaging.SendAsync(new Message
            {
#pragma warning disable CS0618 // FCM registration-token targeting remains the public device contract during FID migration.
                Token = registrationToken,
#pragma warning restore CS0618
                Data = payload.ToData().ToDictionary(pair => pair.Key, pair => pair.Value)
            }, cancellationToken);
            return new(PushSendOutcome.Success);
        }
        catch (FirebaseMessagingException e) when (e.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch)
        { return new(PushSendOutcome.PermanentInvalidRegistration, e.MessagingErrorCode?.ToString()); }
        catch (FirebaseMessagingException e) when (e.MessagingErrorCode is MessagingErrorCode.Unavailable or MessagingErrorCode.Internal or MessagingErrorCode.QuotaExceeded)
        { return new(PushSendOutcome.TransientFailure, e.MessagingErrorCode?.ToString()); }
        catch (FirebaseMessagingException e)
        { return new(PushSendOutcome.PermanentOtherFailure, e.MessagingErrorCode?.ToString() ?? "FirebaseMessaging"); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        { return new(PushSendOutcome.TransientFailure, e.GetType().Name); }
    }
}

public sealed class DisabledPushNotificationSender : IPushNotificationSender
{
    public Task<PushSendResult> SendAsync(string registrationToken, NotificationPayload payload, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PushSendResult(PushSendOutcome.PermanentOtherFailure, "FirebaseDisabled"));
}
