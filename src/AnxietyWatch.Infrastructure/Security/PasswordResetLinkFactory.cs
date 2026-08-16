using AnxietyWatch.Application.Abstractions.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class PasswordResetLinkFactory(
    IConfiguration configuration,
    IHostEnvironment environment) : IPasswordResetLinkFactory
{
    public string Create(string token)
    {
        var configuredUrl = configuration["Email:PasswordResetUrl"];
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var resetUrl) ||
            resetUrl.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(resetUrl.UserInfo) ||
            !environment.IsDevelopment() && resetUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Email:PasswordResetUrl must be a safe absolute HTTPS URL.");
        }

        var builder = new UriBuilder(resetUrl);
        builder.Fragment = $"token={Uri.EscapeDataString(token)}";
        return builder.Uri.AbsoluteUri;
    }
}
