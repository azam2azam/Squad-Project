using Application.Portfolio;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Portfolio-level aggregates for the dashboard. Everything is computed server-side so
/// the headline numbers cannot drift from the boards they summarise.
/// </summary>
[ApiController]
[Route("api/v1/portfolio")]
[Produces("application/json")]
public sealed class PortfolioController(ISender sender) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<PortfolioSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PortfolioSummaryDto>> Summary(CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetPortfolioSummaryQuery(), cancellationToken));
}
