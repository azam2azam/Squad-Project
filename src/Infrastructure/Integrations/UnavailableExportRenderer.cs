using Application.Abstractions;

namespace Infrastructure.Integrations;

/// <summary>
/// Placeholder renderer for hosts with no headless browser. Reports itself unavailable
/// and fails loudly rather than returning an empty file. The Chromium-backed
/// implementation lands in M4.
/// </summary>
public sealed class UnavailableExportRenderer : IExportRenderer
{
    private const string Message =
        "Server-side export is not available on this host. Use the client-side PNG export, " +
        "or configure a headless Chromium renderer.";

    public bool IsAvailable => false;

    public Task<byte[]> RenderPngAsync(ExportRequest request, CancellationToken cancellationToken = default)
        => throw new ExportUnavailableException(Message);

    public Task<byte[]> RenderPdfAsync(ExportRequest request, CancellationToken cancellationToken = default)
        => throw new ExportUnavailableException(Message);

    public Task<byte[]> RenderPortfolioPdfAsync(ExportRequest request,
        CancellationToken cancellationToken = default)
        => throw new ExportUnavailableException(Message);
}
