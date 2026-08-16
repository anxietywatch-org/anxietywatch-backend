using AnxietyWatch.Application.Features.Billing;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AnxietyWatch.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/billing")]
public sealed class BillingController(ISender sender) : ControllerBase
{
    [HttpPost("simulate-payment")]
    public async Task<ActionResult<SimulatedPaymentResponse>> SimulatePayment(
        SimulatePaymentCommand command,
        CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await sender.Send(command, cancellationToken));

    [HttpGet("summary")]
    public async Task<ActionResult<BillingSummaryResponse>> Summary(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetBillingSummaryQuery(), cancellationToken));

    [HttpGet("transactions")]
    public async Task<ActionResult<IReadOnlyList<SimulatedPaymentResponse>>> Transactions(CancellationToken cancellationToken) =>
        Ok((await sender.Send(new GetBillingSummaryQuery(), cancellationToken)).Transactions);
}
