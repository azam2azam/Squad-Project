using System.Text.Json;
using Application.Portability;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Bulk JSON export and import — the production equivalent of the prototype's
/// Save and Load (spec FR-9).
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class PortabilityController(ISender sender) : ControllerBase
{
    private static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>Downloads every board and the whole roster as a single JSON file.</summary>
    [HttpGet("export")]
    [ProducesResponseType<BoardExportFile>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportDataQuery(), cancellationToken);

        var json = JsonSerializer.SerializeToUtf8Bytes(file, FileJson);
        var name = $"squad-status-board-{DateTime.UtcNow:yyyy-MM-dd}.json";

        return File(json, "application/json", name);
    }

    /// <summary>
    /// Restores boards and roster from an exported file. Upserts by id, so importing
    /// the same file twice changes nothing the second time.
    /// </summary>
    [HttpPost("import")]
    [ProducesResponseType<ImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ImportResult>> Import(
        [FromBody] BoardExportFile file, CancellationToken cancellationToken)
        => Ok(await sender.Send(new ImportDataCommand(file), cancellationToken));
}
