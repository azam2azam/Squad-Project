using Application.Abstractions;
using Application.Boards.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Server-side rendering of slides to PNG and PDF at 2x (spec section 10).
///
/// The renderer loads the web app's standalone slide route, so exports go through the
/// same <c>SlideCanvas</c> component the user sees — there is no second implementation
/// of the slide to keep in step.
/// </summary>
[ApiController]
[Route("api/v1")]
public sealed class ExportsController(
    ISender sender,
    IExportRenderer renderer,
    IConfiguration configuration) : ControllerBase
{
    private string SlideBaseUrl =>
        configuration["Export:SlideBaseUrl"]?.TrimEnd('/')
        ?? throw new InvalidOperationException("Export:SlideBaseUrl is not configured.");

    private int Scale => configuration.GetValue("Export:Scale", 2);

    [HttpGet("boards/{id:guid}/export/png")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> BoardPng(Guid id, CancellationToken cancellationToken)
    {
        var board = await sender.Send(new GetBoardQuery(id), cancellationToken);

        var bytes = await renderer.RenderPngAsync(
            new ExportRequest($"{SlideBaseUrl}/slide/{id}", Scale: Scale), cancellationToken);

        return File(bytes, "image/png", $"{Slugify(board.Title)}-status.png");
    }

    [HttpGet("boards/{id:guid}/export/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> BoardPdf(Guid id, CancellationToken cancellationToken)
    {
        var board = await sender.Send(new GetBoardQuery(id), cancellationToken);

        var bytes = await renderer.RenderPdfAsync(
            new ExportRequest($"{SlideBaseUrl}/slide/{id}", Scale: Scale), cancellationToken);

        return File(bytes, "application/pdf", $"{Slugify(board.Title)}-status.pdf");
    }

    /// <summary>Every board, one slide per page, in a single PDF.</summary>
    [HttpGet("portfolio/export/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileResult))]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> PortfolioPdf(CancellationToken cancellationToken)
    {
        var bytes = await renderer.RenderPortfolioPdfAsync(
            new ExportRequest($"{SlideBaseUrl}/slide/all", Scale: Scale), cancellationToken);

        return File(bytes, "application/pdf",
            $"squad-portfolio-{DateTime.UtcNow:yyyy-MM-dd}.pdf");
    }

    /// <summary>"OPD Screen Revamp" -> "opd-screen-revamp", matching the prototype's naming.</summary>
    private static string Slugify(string value)
    {
        var slug = new string(value
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        return slug.Trim('-') is { Length: > 0 } trimmed ? trimmed : "squad";
    }
}
