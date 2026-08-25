using System.Net.Http;
using AnxietyWatch.Domain.Plans;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Application.Features.Support;
using AnxietyWatch.Infrastructure.Caching;
using AnxietyWatch.Infrastructure.MlInference;
using AnxietyWatch.Infrastructure.Notifications;
using AnxietyWatch.Infrastructure.Persistence;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using AnxietyWatch.Infrastructure.Security;
using AnxietyWatch.Infrastructure.Wearables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AnxietyWatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Caching.ICacheService, NoOpCacheService>();
        services.AddHttpContextAccessor();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IJwtTokenService, JwtTokenService>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IEmailVerificationLinkFactory, EmailVerificationLinkFactory>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IPasswordResetLinkFactory, PasswordResetLinkFactory>();
        services.AddSingleton<PasswordRecoveryEmailQueue>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IPasswordRecoveryEmailQueue>(serviceProvider =>
            serviceProvider.GetRequiredService<PasswordRecoveryEmailQueue>());
        services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<PasswordRecoveryEmailQueue>());
        var emailProvider = configuration["Email:Provider"];
        if (environment.IsProduction() &&
            (!string.Equals(emailProvider, "Resend", StringComparison.OrdinalIgnoreCase) ||
             string.IsNullOrWhiteSpace(configuration["Email:Resend:ApiKey"]) ||
             string.IsNullOrWhiteSpace(configuration["Email:From"])))
        {
            throw new InvalidOperationException("Resend email delivery must be fully configured in Production.");
        }

        if (string.Equals(emailProvider, "Resend", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<AnxietyWatch.Application.Abstractions.Security.IEmailSender, ResendEmailSender>(client =>
            {
                client.BaseAddress = new Uri("https://api.resend.com/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else
        {
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IEmailSender, LoggingEmailSender>();
        }
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Time.ISystemClock, SystemClock>();
        services.AddHttpClient<AnxietyWatch.Application.Abstractions.Notifications.IPushNotifier, PushNotifier>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Notifications.ICaregiverAlertDispatcher, CaregiverAlertDispatcher>();

        services.AddHttpClient<AnxietyWatch.Application.Abstractions.MlInference.IMlInferenceClient, MlInferenceHttpClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(GetMlInferenceTimeoutSeconds(configuration));
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false
        });
        services.AddTransient<AnxietyWatch.Application.Features.Wearables.ISuspectedEventInferenceService, SuspectedEventInferenceService>();

        if (string.Equals(configuration["Persistence:Provider"], "Mongo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<MongoContext>();
            services.AddSingleton<IPlanRepository, MongoPlanRepository>();
            services.AddSingleton<AnxietyWatch.Application.Features.Wearables.IWearableSyncRepository, MongoWearableSyncRepository>();
             services.AddSingleton<AnxietyWatch.Application.Features.Wearables.IEventInferenceRepository, MongoEventInferenceRepository>();
             services.AddSingleton<AnxietyWatch.Application.Features.Caregivers.IPatientEventRepository, MongoPatientEventRepository>();
            services.AddSingleton<ISupportTicketRepository, MongoSupportTicketRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Billing.IBillingTransactionRepository, MongoBillingTransactionRepository>();
            services.AddSingleton<IUserRepository, MongoUserRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Episodes.IEpisodeRepository, MongoEpisodeRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Tokens.ILinkTokenRepository, MongoLinkTokenRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Devices.IDeviceTokenRepository, MongoDeviceTokenRepository>();
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IRevokedTokenStore, MongoRevokedTokenStore>();
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IPasswordResetTokenStore, MongoPasswordResetTokenStore>();
        }
        else
        {
            services.AddSingleton<IPlanRepository, InMemoryPlanRepository>();
             services.AddSingleton<InMemoryWearableSyncRepository>();
             services.AddSingleton<AnxietyWatch.Application.Features.Wearables.IWearableSyncRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryWearableSyncRepository>());
             services.AddSingleton<AnxietyWatch.Application.Features.Wearables.IEventInferenceRepository, InMemoryEventInferenceRepository>();
             services.AddSingleton<AnxietyWatch.Application.Features.Caregivers.IPatientEventRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryWearableSyncRepository>());
            services.AddSingleton<ISupportTicketRepository, InMemorySupportTicketRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Billing.IBillingTransactionRepository, InMemoryBillingTransactionRepository>();
            services.AddSingleton<IUserRepository, InMemoryUserRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Episodes.IEpisodeRepository, InMemoryEpisodeRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Tokens.ILinkTokenRepository, InMemoryLinkTokenRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Devices.IDeviceTokenRepository, InMemoryDeviceTokenRepository>();
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IRevokedTokenStore, InMemoryRevokedTokenStore>();
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IPasswordResetTokenStore, InMemoryPasswordResetTokenStore>();
        }

        return services;
    }

    private static int GetMlInferenceTimeoutSeconds(IConfiguration configuration) =>
        int.TryParse(configuration["Ml:Inference:TimeoutSeconds"], out var seconds) && seconds > 0
            ? seconds
            : 10;
}
