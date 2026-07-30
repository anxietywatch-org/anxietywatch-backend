using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AnxietyWatch.Application.Abstractions.Security;
using Microsoft.AspNetCore.Http;

namespace AnxietyWatch.Infrastructure.Security;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(
        Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
        out var userId) ? userId : Guid.Empty;

    public string? Email => Principal.FindFirstValue(JwtRegisteredClaimNames.Email) ??
                            Principal.FindFirstValue(ClaimTypes.Email);

    public string? PlanId => Principal.FindFirstValue("plan");

    public string? JwtId => Principal.FindFirstValue(JwtRegisteredClaimNames.Jti);

    public DateTimeOffset? TokenExpiresAt => long.TryParse(
        Principal.FindFirstValue(JwtRegisteredClaimNames.Exp), out var seconds)
        ? DateTimeOffset.FromUnixTimeSeconds(seconds)
        : null;
}
