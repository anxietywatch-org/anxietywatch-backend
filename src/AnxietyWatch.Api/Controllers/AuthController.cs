using AnxietyWatch.Application.Features.Authentication;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthenticationResponse>> Register(
        [FromBody] RegisterCommand command,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthenticationResponse>> Login(
        LoginCommand command,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(command, cancellationToken));

    [Authorize]
    [HttpGet("session")]
    public async Task<ActionResult<AuthenticationResponse>> Session(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSessionQuery(), cancellationToken));

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sender.Send(new LogoutCommand(), cancellationToken);
        return Ok(new { success = true });
    }

    [HttpPost("password/forgot")]
    public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(command, cancellationToken));

    [HttpPost("password/reset")]
    public async Task<ActionResult<object>> ResetPassword(
        ResetPasswordCommand command,
        CancellationToken cancellationToken) =>
        Ok(new { message = await sender.Send(command, cancellationToken) });

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<object>> ChangePassword(
        ChangePasswordCommand command,
        CancellationToken cancellationToken) =>
        Ok(new { message = await sender.Send(command, cancellationToken) });

    [Authorize]
    [HttpGet("verify-email/status")]
    public async Task<ActionResult<EmailVerificationStatusResponse>> VerificationStatus(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetEmailVerificationStatusQuery(), cancellationToken));

    [Authorize]
    [HttpPost("verify-email/resend")]
    public async Task<ActionResult<object>> ResendVerification(
        CancellationToken cancellationToken) =>
        Ok(new { message = await sender.Send(new ResendVerificationEmailCommand(), cancellationToken) });
}
