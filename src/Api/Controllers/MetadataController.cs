using Application.Abstractions;
using Application.Boards.Queries;
using Application.Contracts;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Reference data for the client: role and status options with their canonical labels
/// and design-token colours. The web app renders from this rather than hardcoding
/// the palette in two places.
/// </summary>
[ApiController]
[Route("api/v1/metadata")]
public sealed class MetadataController(
    IJiraClient jiraClient, IExportRenderer exportRenderer, ISender sender)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MetadataDto>(StatusCodes.Status200OK)]
    public ActionResult<MetadataDto> Get() => Ok(MetadataDto.Build());

    /// <summary>Which optional capabilities this deployment actually has.</summary>
    [HttpGet("capabilities")]
    public ActionResult<object> GetCapabilities() => Ok(new
    {
        jiraSyncEnabled = jiraClient.IsEnabled,
        serverExportEnabled = exportRenderer.IsAvailable,
        // Excel is always available: it needs no external service, unlike the other two.
        excelEnabled = true
    });

    /// <summary>
    /// Tests the Jira connection for real, so an admin can tell "not configured" from
    /// "configured but the token is wrong". Admin-only — it spends our credentials.
    /// </summary>
    [HttpGet("jira/connection")]
    [ProducesResponseType<JiraConnectionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JiraConnectionDto>> GetJiraConnection(
        [FromQuery] string? projectKey, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetJiraConnectionQuery(projectKey), cancellationToken));
}
