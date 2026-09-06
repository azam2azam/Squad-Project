using Application.Analytics;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Comparative analytics: squads beside each other, progress week by week, and how loaded
/// each person is. Read-only, and open to anyone signed in — a Viewer's whole job is
/// reading this.
/// </summary>
[ApiController]
[Route("api/v1/analytics")]
[Produces("application/json")]
public sealed class AnalyticsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AnalyticsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AnalyticsDto>> Get(
        [FromQuery] int weeks = 12, CancellationToken cancellationToken = default)
        => Ok(await sender.Send(new GetAnalyticsQuery(weeks), cancellationToken));
}
