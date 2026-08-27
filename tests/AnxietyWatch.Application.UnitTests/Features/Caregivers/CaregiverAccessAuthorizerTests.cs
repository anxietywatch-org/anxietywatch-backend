using AnxietyWatch.Application.Abstractions.Security;
using AnxietyWatch.Application.Common;
using AnxietyWatch.Application.Features.Caregivers;
using AnxietyWatch.Domain.Tokens;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AnxietyWatch.Application.UnitTests.Features.Caregivers;

public sealed class CaregiverAccessAuthorizerTests
{
    private readonly ICurrentUser currentUser = Substitute.For<ICurrentUser>();
    private readonly ILinkTokenRepository tokens = Substitute.For<ILinkTokenRepository>();
    private readonly CaregiverAccessAuthorizer authorizer;

    public CaregiverAccessAuthorizerTests()
    {
        currentUser.IsAuthenticated.Returns(true);
        currentUser.UserId.Returns(Guid.Parse("6cab73e8-d0f7-4b22-8ff1-1516351caaba"));
        authorizer = new CaregiverAccessAuthorizer(currentUser, tokens, NullLogger<CaregiverAccessAuthorizer>.Instance);
    }

    [Fact]
    public async Task RequireCaregiverAccessAsync_WhenAcceptedRelationshipExists_ShouldPass()
    {
        var patientId = Guid.NewGuid();
        tokens.HasAcceptedCaregiverRelationshipAsync(patientId, currentUser.UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        await authorizer.RequireCaregiverAccessAsync(patientId);

        await tokens.Received(1).HasAcceptedCaregiverRelationshipAsync(
            patientId,
            currentUser.UserId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequireCaregiverAccessAsync_WhenUnauthenticated_ShouldThrowUnauthorized()
    {
        currentUser.IsAuthenticated.Returns(false);

        var act = async () => await authorizer.RequireCaregiverAccessAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<UnauthorizedApplicationException>()
            .WithMessage("Authentication is required.");
    }

    [Fact]
    public async Task RequireCaregiverAccessAsync_WhenNoAcceptedRelationshipExists_ShouldThrowForbidden()
    {
        var patientId = Guid.NewGuid();
        tokens.HasAcceptedCaregiverRelationshipAsync(patientId, currentUser.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        var act = async () => await authorizer.RequireCaregiverAccessAsync(patientId);

        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("The authenticated caregiver cannot access this patient.");
    }
}
