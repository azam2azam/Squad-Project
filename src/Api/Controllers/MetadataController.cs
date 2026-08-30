using Application.Abstractions;
using Application.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Reference data for the client: role and status options with their canonical labels
/// and design-token colours. The web app renders from this rather than hardcoding
/// the palette in two places.
/// </summary>
[ApiController]
[Route("api/v1/metadata")]
public sealed class MetadataController(IJiraClient jiraClient, IExportRenderer exportRenderer)
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
        serverExportEnabled = exportRenderer.IsAvailable
    });
}
