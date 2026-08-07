using AnxietyWatch.Application;
using AnxietyWatch.Infrastructure;
using AnxietyWatch.Application.Abstractions.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;

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
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNameCaseInsensitive = true);
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
                }
            }
        };
    });

var app = builder.Build();

app.UseMiddleware<AnxietyWatch.Api.Middleware.ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "AnxietyWatch.API",
    timestamp = DateTime.UtcNow
})).ExcludeFromDescription();

app.Run();

public partial class Program;
