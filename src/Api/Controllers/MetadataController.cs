using Application.Abstractions;
using Application.Boards.Queries;
using Application.Contracts;
using Application.Roles;
using Domain.Enums;
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
    public async Task<ActionResult<MetadataDto>> Get(CancellationToken cancellationToken)
    {
        // Read from the database rather than the in-process catalogue: roles are
        // configurable, and this list drives every picker, so it must be current and must
        // exclude retired roles even if another instance has not refreshed yet.
        var roles = await sender.Send(new ListRolesQuery(), cancellationToken);

        return Ok(MetadataDto.Build(
            roles
                .Select(r => new RoleOptionDto((Role)r.Value, r.Name, r.Label, r.Color))
                .ToList()));
    }

    /// <summary>Which optional capabilities this deployment actually has.</summary>
    [HttpGet("capabilities")]
    public async Task<ActionResult<object>> GetCapabilities(CancellationToken cancellationToken)
        => Ok(new
        {
            jiraSyncEnabled = await jiraClient.IsEnabledAsync(cancellationToken),
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
