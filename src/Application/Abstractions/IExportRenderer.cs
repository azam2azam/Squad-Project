namespace Application.Abstractions;

/// <summary>
/// Renders a SlideCanvas to a boardroom-quality raster or PDF. Backed by headless
/// Chromium in production; swappable and mockable for tests and constrained hosts.
/// </summary>
public interface IExportRenderer
{
    /// <summary>True when a rendering engine is actually available on this host.</summary>
    bool IsAvailable { get; }

    Task<byte[]> RenderPngAsync(ExportRequest request, CancellationToken cancellationToken = default);

    Task<byte[]> RenderPdfAsync(ExportRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Multi-page PDF for the portfolio export. Takes a single URL pointing at a route
    /// that stacks every slide with page breaks between them, so the renderer paginates
    /// in one pass rather than producing N documents that then need merging.
    /// </summary>
    Task<byte[]> RenderPortfolioPdfAsync(ExportRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One slide to render. <paramref name="Url"/> points at the standalone SlideCanvas route
/// which the renderer loads and screenshots.
/// </summary>
/// <param name="Url">Absolute URL of the isolated slide route.</param>
/// <param name="WidthPx">CSS width of the slide viewport.</param>
/// <param name="HeightPx">CSS height of the slide viewport.</param>
/// <param name="Scale">Device pixel ratio; 2 for boardroom quality.</param>
public sealed record ExportRequest(string Url, int WidthPx = 1280, int HeightPx = 720, int Scale = 2);

/// <summary>Raised when an export is requested on a host with no rendering engine.</summary>
public sealed class ExportUnavailableException(string message) : Exception(message);
