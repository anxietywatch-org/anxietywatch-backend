using AnxietyWatch.Application.Abstractions.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class EmailVerificationLinkFactory(
    IConfiguration configuration,
    IHostEnvironment environment) : IEmailVerificationLinkFactory
{
    public string Create(string token)
    {
        var configuredUrl = configuration["Email:VerificationUrl"];
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var verificationUrl) ||
            verificationUrl.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(verificationUrl.UserInfo) ||
            !environment.IsDevelopment() && verificationUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Email:VerificationUrl must be a safe absolute HTTPS URL.");
        }

        var builder = new UriBuilder(verificationUrl);
        builder.Fragment = $"token={Uri.EscapeDataString(token)}";
        return builder.Uri.AbsoluteUri;
    }
}
