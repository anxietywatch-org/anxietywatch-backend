using AnxietyWatch.Application.Features.Support;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/support/tickets")]
public sealed class SupportController(ISender sender) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("support-tickets")]
    public async Task<ActionResult<SupportTicketResponse>> Create(
        CreateSupportTicketCommand command,
        CancellationToken cancellationToken)
    {
        var response = await sender.Send(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupportTicketResponse>>> Get(
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSupportTicketsQuery(), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupportTicketResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetSupportTicketQuery(id), cancellationToken));
}
