using System.Text.Json;
using Application.Abstractions;
using Application.Portability;
using MediatR;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Bulk JSON export and import — the production equivalent of the prototype's
/// Save and Load (spec FR-9).
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class PortabilityController(ISender sender, IWorkbookSerializer workbooks)
    : ControllerBase
{
    /// <summary>20 MB — comfortably above a large portfolio, well below a memory risk.</summary>
    private const int MaxUploadBytes = 20 * 1024 * 1024;

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

    /// <summary>
    /// The same export as an Excel workbook: Boards, People and Members on separate
    /// sheets, with a "Read me" explaining how to edit and re-import it.
    /// </summary>
    [HttpGet("export/excel")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    public async Task<IActionResult> ExportExcel(CancellationToken cancellationToken)
    {
        var file = await sender.Send(new ExportDataQuery(), cancellationToken);
        var bytes = workbooks.Write(file);

        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"squad-status-board-{DateTime.UtcNow:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Reads an edited workbook back. Goes through the same upsert-by-id pipeline as the
    /// JSON import, so the two cannot behave differently.
    /// </summary>
    [HttpPost("import/excel")]
    [ProducesResponseType<ImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(MaxUploadBytes)]
    // RequestSizeLimit alone is not enough: the multipart body limit defaults to 16 KB,
    // which any real workbook exceeds. Both have to be raised.
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    public async Task<ActionResult<ImportResult>> ImportExcel(
        IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "No file was uploaded"
            });
        }

        await using var stream = file.OpenReadStream();
        var parsed = workbooks.Read(stream);

        return Ok(await sender.Send(new ImportDataCommand(parsed), cancellationToken));
    }
}
