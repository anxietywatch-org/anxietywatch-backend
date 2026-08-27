using AnxietyWatch.Domain.Notifications;
using AnxietyWatch.Infrastructure.Notifications;
using FluentAssertions;

namespace AnxietyWatch.Application.UnitTests.Features.Notifications;

public sealed class FirebasePushNotificationSenderTests
{
    [Fact]
    public void BuildMessage_UsesRegistrationTokenTargetAndOnlyRequiredData()
    {
        var registrationToken = "registration-token";
        var message = FirebasePushNotificationSender.BuildMessage(
            registrationToken,
            new NotificationPayload("event-1", "Patient", "SOS alert"));

#pragma warning disable CS0618 // The test verifies the registration-token compatibility property explicitly.
        message.Token.Should().Be(registrationToken);
#pragma warning restore CS0618
        message.Fid.Should().BeNullOrEmpty();
        message.Data.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["eventId"] = "event-1",
            ["patientName"] = "Patient",
            ["alertMessage"] = "SOS alert"
        });
    }

    [Fact]
    public void BuildMessage_IncludesOnlyPersistedOptionalData()
    {
        var message = FirebasePushNotificationSender.BuildMessage(
            "registration-token",
            new NotificationPayload("event-1", "Patient", "Support requested", "Home", "+1-555-0100"));

        message.Data.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["eventId"] = "event-1",
            ["patientName"] = "Patient",
            ["alertMessage"] = "Support requested",
            ["location"] = "Home",
            ["emergencyPhone"] = "+1-555-0100"
        });
    }
}
