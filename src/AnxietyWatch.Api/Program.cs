using AnxietyWatch.Application;
using AnxietyWatch.Infrastructure;
using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Domain.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException("Jwt:SigningKey must be provided by a secret manager in Production.");
    }

    signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    builder.Configuration["Jwt:SigningKey"] = signingKey;
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNameCaseInsensitive = true);
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration["Cors:AllowedOrigins"]
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (origins is { Length: > 0 })
        {
            policy.WithOrigins(origins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("password-recovery", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("support-tickets", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("FamilyPlan", policy => policy.RequireClaim("plan", "family"));
    options.AddPolicy("ProfessionalPlan", policy => policy.RequireClaim("plan", "professional"));
    options.AddPolicy("IndividualOrHigher", policy => policy.RequireClaim(
        "plan", "individual", "family", "professional"));
});
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateIssuer = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Issuer"]),
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = !string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Audience"]),
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var jwtId = context.Principal?.FindFirst("jti")?.Value;
                if (string.IsNullOrWhiteSpace(jwtId))
                {
                    context.Fail("JWT is missing jti.");
                    return;
                }

                var revokedTokens = context.HttpContext.RequestServices.GetRequiredService<IRevokedTokenStore>();
                if (await revokedTokens.IsRevokedAsync(jwtId, context.HttpContext.RequestAborted))
                {
                    context.Fail("JWT has been revoked.");
                    return;
                }

                var userIdValue = context.Principal?.FindFirst("sub")?.Value;
                var securityVersionValue = context.Principal?.FindFirst("security_version")?.Value;
                if (!Guid.TryParse(userIdValue, out var userId) ||
                    !long.TryParse(securityVersionValue ?? "0", out var securityVersion))
                {
                    context.Fail("JWT security state is invalid.");
                    return;
                }

                var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                var user = await users.GetByIdAsync(userId, context.HttpContext.RequestAborted);
                if (user is null || user.SecurityVersion != securityVersion)
                {
                    context.Fail("JWT security state is stale.");
                }
            }
        };
    });

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<AnxietyWatch.Api.Middleware.ExceptionHandlingMiddleware>();
if (!app.Environment.IsProduction() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "AnxietyWatch.API",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
})).ExcludeFromDescription();

app.Run();

public partial class Program;
