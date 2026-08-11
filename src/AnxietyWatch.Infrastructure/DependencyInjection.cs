using AnxietyWatch.Domain.Plans;
using AnxietyWatch.Domain.Users;
using AnxietyWatch.Infrastructure.Caching;
using AnxietyWatch.Infrastructure.Persistence;
using AnxietyWatch.Infrastructure.Persistence.Mongo;
using AnxietyWatch.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AnxietyWatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Caching.ICacheService, NoOpCacheService>();
        services.AddHttpContextAccessor();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IJwtTokenService, JwtTokenService>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IEmailSender, LoggingEmailSender>();
        services.AddSingleton<AnxietyWatch.Application.Abstractions.Time.ISystemClock, SystemClock>();

        if (string.Equals(configuration["Persistence:Provider"], "Mongo", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<MongoContext>();
            services.AddHostedService<MongoIndexInitializer>();
            services.AddSingleton<IUserRepository, MongoUserRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Episodes.IEpisodeRepository, MongoEpisodeRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Tokens.ILinkTokenRepository, MongoLinkTokenRepository>();
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IRevokedTokenStore, MongoRevokedTokenStore>();
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IPasswordResetTokenStore, MongoPasswordResetTokenStore>();
            services.AddSingleton<IPlanRepository, MongoPlanRepository>();
            services.AddSingleton<AnxietyWatch.Application.Features.Wearables.IWearableSyncRepository, MongoWearableSyncRepository>();
        }
        else
        {
            services.AddSingleton<IUserRepository, InMemoryUserRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Episodes.IEpisodeRepository, InMemoryEpisodeRepository>();
            services.AddSingleton<AnxietyWatch.Domain.Tokens.ILinkTokenRepository, InMemoryLinkTokenRepository>();
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IRevokedTokenStore, InMemoryRevokedTokenStore>();
            services.AddSingleton<AnxietyWatch.Application.Abstractions.Security.IPasswordResetTokenStore, InMemoryPasswordResetTokenStore>();
            services.AddSingleton<IPlanRepository, InMemoryPlanRepository>();
            services.AddSingleton<AnxietyWatch.Application.Features.Wearables.IWearableSyncRepository, InMemoryWearableSyncRepository>();
        }

        return services;
    }
}
