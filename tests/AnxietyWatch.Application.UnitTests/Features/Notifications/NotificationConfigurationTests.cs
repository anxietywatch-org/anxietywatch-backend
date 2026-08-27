using AnxietyWatch.Application.Abstractions.Notifications;
using AnxietyWatch.Infrastructure;
using AnxietyWatch.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace AnxietyWatch.Application.UnitTests.Features.Notifications;

public sealed class NotificationConfigurationTests
{
    [Fact]
    public void FirebaseAndWorkerDisabled_IsAllowedOutsideProduction()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(Configuration(firebaseEnabled: false, workerEnabled: false), Environment());

        services.Should().NotBeNull();
    }

    [Fact]
    public void WorkerEnabledWithoutFirebase_FailsClosed()
    {
        var act = () => new ServiceCollection().AddInfrastructure(
            Configuration(firebaseEnabled: false, workerEnabled: true), Environment());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Notifications worker requires Firebase:Enabled=true.");
    }

    [Fact]
    public void FirebaseEnabledWithoutCredentialSource_FailsClosed()
    {
        var act = () => new ServiceCollection().AddInfrastructure(
            Configuration(firebaseEnabled: true, workerEnabled: false), Environment());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Firebase requires exactly one credential source: Firebase:CredentialsPath or Firebase:CredentialsJson.");
    }

    [Fact]
    public void FirebaseEnabledWithBothCredentialSources_FailsClosed()
    {
        var act = () => new ServiceCollection().AddInfrastructure(
            Configuration(firebaseEnabled: true, workerEnabled: false, path: "/run/secrets/firebase.json", json: "{}"), Environment());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FirebaseEnabledWithExactlyOneCredentialSource_RegistersFirebaseSender()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure(Configuration(firebaseEnabled: true, workerEnabled: false, path: "/run/secrets/firebase.json"), Environment());

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IPushNotificationSender) &&
            descriptor.ImplementationType == typeof(FirebasePushNotificationSender));
    }

    private static IConfiguration Configuration(bool firebaseEnabled, bool workerEnabled, string? path = null, string? json = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Firebase:Enabled"] = firebaseEnabled.ToString(),
            ["Notifications:WorkerEnabled"] = workerEnabled.ToString(),
            ["Firebase:CredentialsPath"] = path,
            ["Firebase:CredentialsJson"] = json,
            ["Email:Provider"] = "Logging",
            ["Persistence:Provider"] = "InMemory"
        }).Build();

    private static IHostEnvironment Environment() => new HostingEnvironment
    {
        EnvironmentName = "Testing",
        ApplicationName = typeof(NotificationConfigurationTests).Assembly.GetName().Name ?? "NotificationConfigurationTests",
        ContentRootPath = Directory.GetCurrentDirectory()
    };
}
