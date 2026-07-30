using System.Text;
using AnxietyWatch.Application.Features.Tokens;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tokens")]
public sealed class TokensController(ISender sender) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TokenResponse>> Create(
        CreateTokenCommand command,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TokenResponse>>> Get(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetTokensQuery(), cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteTokenCommand(id), cancellationToken);
        return Ok(new { success = true });
    }

    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult<object>> Accept(
        Guid id,
        AcceptTokenRequest request,
        CancellationToken cancellationToken)
        => Ok(new { status = await sender.Send(new AcceptTokenCommand(id, request.DeviceId), cancellationToken) });

    [HttpPost("{id:guid}/share")]
    public async Task<ActionResult<object>> Share(
        Guid id,
        ShareTokenRequest request,
        CancellationToken cancellationToken)
        => Ok(new { sent = await sender.Send(new ShareTokenCommand(id, request.RecipientEmail), cancellationToken) });

    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var tokens = await sender.Send(new GetTokensQuery(), cancellationToken);
        var csv = new StringBuilder("id,code,status,role,expiresAt\n");
        foreach (var token in tokens)
        {
            csv.AppendLine(string.Join(',', token.Id, token.Code, token.Status, token.Role, token.ExpiresAt));
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "tokens.csv");
    }
}

public sealed record AcceptTokenRequest(string DeviceId);

public sealed record ShareTokenRequest(string RecipientEmail);
