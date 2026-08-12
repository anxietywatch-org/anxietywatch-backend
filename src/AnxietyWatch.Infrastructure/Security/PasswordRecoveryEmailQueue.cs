using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Abstractions.Time;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class PasswordRecoveryEmailQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordRecoveryEmailQueue> logger) : BackgroundService, IPasswordRecoveryEmailQueue
{
    private readonly Channel<string> queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly ConcurrentDictionary<string, byte> pendingEmails = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> nextAllowedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly object cooldownGate = new();

    public bool TryQueue(string normalizedEmail)
    {
        var now = DateTimeOffset.UtcNow;
        lock (cooldownGate)
        {
            if (nextAllowedAt.TryGetValue(normalizedEmail, out var nextAllowed) && nextAllowed > now)
            {
                return true;
            }
        }

        if (!pendingEmails.TryAdd(normalizedEmail, 0))
        {
            return true;
        }

        var queued = queue.Writer.TryWrite(normalizedEmail);
        if (!queued)
        {
            pendingEmails.TryRemove(normalizedEmail, out _);
            logger.LogWarning("Password recovery email queue is full.");
        }
        else
        {
            lock (cooldownGate)
            {
                RemoveExpiredCooldowns(now);
                if (nextAllowedAt.Count >= 1024)
                {
                    var oldest = nextAllowedAt.MinBy(pair => pair.Value);
                    nextAllowedAt.Remove(oldest.Key);
                }

                nextAllowedAt[normalizedEmail] = now.AddMinutes(1);
            }
        }

        return queued;
    }

    private void RemoveExpiredCooldowns(DateTimeOffset now)
    {
        foreach (var email in nextAllowedAt
                     .Where(pair => pair.Value <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            nextAllowedAt.Remove(email);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(email, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (EmailDeliveryException exception)
            {
                logger.LogWarning(exception, "Password recovery email delivery failed.");
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unexpected password recovery email failure.");
            }
            finally
            {
                pendingEmails.TryRemove(email, out _);
            }
        }
    }

    private async Task ProcessAsync(string email, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var user = await users.GetByEmailAsync(email, cancellationToken);
        if (user is null) return;

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var clock = scope.ServiceProvider.GetRequiredService<ISystemClock>();
        var resetTokens = scope.ServiceProvider.GetRequiredService<IPasswordResetTokenStore>();
        await resetTokens.StoreAsync(
            tokenHash,
            user.Id,
            clock.UtcNow.AddMinutes(30),
            cancellationToken);
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        await emailSender.SendAsync(
            user.Email,
            "AnxietyWatch password recovery",
            rawToken,
            cancellationToken);
    }
}
