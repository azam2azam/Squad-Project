using Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Infrastructure.Integrations;

/// <summary>
/// Renders the standalone SlideCanvas route to PNG or PDF with headless Chromium.
///
/// The browser is acquired lazily and once: the first export pays for the download or
/// launch, later ones reuse the instance. If Chromium cannot be obtained — no network,
/// a locked-down host — the renderer reports itself unavailable rather than failing
/// every request, and the client hides the affordance instead of offering a button
/// that errors.
/// </summary>
public sealed class ChromiumExportRenderer : IExportRenderer, IAsyncDisposable
{
    /// <summary>The slide surface itself — what an export should contain, and nothing else.</summary>
    private const string SlideSelector = "app-slide-canvas .slide";

    private readonly ILogger<ChromiumExportRenderer> _logger;
    private readonly string? _executablePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IBrowser? _browser;
    private bool _unavailable;

    public ChromiumExportRenderer(IConfiguration configuration,
        ILogger<ChromiumExportRenderer> logger)
    {
        _logger = logger;
        // Set Export:ChromiumPath to use a browser already on the host and skip the
        // download entirely — the usual choice in a locked-down or container image.
        _executablePath = configuration["Export:ChromiumPath"];
    }

    /// <summary>False once an attempt to obtain Chromium has definitively failed.</summary>
    public bool IsAvailable => !_unavailable;

    public async Task<byte[]> RenderPngAsync(ExportRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var page = await NewPageAsync(request, cancellationToken);

        // Clipped to the slide itself rather than the viewport. The slide is
        // content-height, so a fixed-height capture would pad the image with dead
        // space below a short squad.
        var slide = await page.QuerySelectorAsync(SlideSelector);
        if (slide is not null)
        {
            return await slide.ScreenshotDataAsync(new ElementScreenshotOptions
            {
                Type = ScreenshotType.Png,
                OmitBackground = false
            });
        }

        return await page.ScreenshotDataAsync(new ScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true,
            OmitBackground = false
        });
    }

    public async Task<byte[]> RenderPdfAsync(ExportRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var page = await NewPageAsync(request, cancellationToken);

        // Page sized to the tallest slide so the PDF is full-bleed slides, not
        // letter-sized sheets with a slide floating in the middle.
        var height = await MeasureSlideHeightAsync(page) ?? request.HeightPx;

        return await page.PdfDataAsync(new PdfOptions
        {
            PrintBackground = true,
            Width = $"{request.WidthPx}px",
            Height = $"{height}px",
            MarginOptions = new MarginOptions
            {
                Top = "0", Bottom = "0", Left = "0", Right = "0"
            }
        });
    }

    /// <summary>
    /// Tallest slide on the page, in CSS pixels. Uniform page height matters for the
    /// portfolio PDF, where slides with different squad sizes must still paginate
    /// one-per-page.
    /// </summary>
    private static async Task<int?> MeasureSlideHeightAsync(IPage page)
    {
        var height = await page.EvaluateFunctionAsync<double?>(
            @"(selector) => {
                const nodes = [...document.querySelectorAll(selector)];
                if (nodes.length === 0) return null;
                return Math.ceil(Math.max(...nodes.map(n => n.getBoundingClientRect().height)));
            }",
            SlideSelector);

        return height is > 0 ? (int)height.Value : null;
    }

    /// <summary>
    /// The portfolio route stacks every slide with a CSS page break between them, so
    /// this is the same single-pass render as one slide — Chromium handles pagination
    /// and no PDF concatenation library is needed.
    /// </summary>
    public Task<byte[]> RenderPortfolioPdfAsync(ExportRequest request,
        CancellationToken cancellationToken = default)
        => RenderPdfAsync(request, cancellationToken);

    private async Task<IPage> NewPageAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        var browser = await GetBrowserAsync(cancellationToken);

        var page = await browser.NewPageAsync();
        await page.SetViewportAsync(new ViewPortOptions
        {
            Width = request.WidthPx,
            Height = request.HeightPx,
            DeviceScaleFactor = request.Scale
        });

        var response = await page.GoToAsync(request.Url, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.Networkidle0],
            Timeout = 30_000
        });

        if (response is not null && !response.Ok)
        {
            await page.CloseAsync();
            throw new ExportUnavailableException(
                $"The slide page returned {response.Status} and could not be rendered.");
        }

        // Web fonts must be resolved before the screenshot or the display face falls
        // back and the export does not match the screen.
        await page.EvaluateExpressionAsync("document.fonts ? document.fonts.ready : true");

        return page;
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is { IsClosed: false })
        {
            return _browser;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_browser is { IsClosed: false })
            {
                return _browser;
            }

            var options = new LaunchOptions
            {
                Headless = true,
                Args = ["--no-sandbox", "--disable-dev-shm-usage", "--disable-gpu"]
            };

            if (!string.IsNullOrWhiteSpace(_executablePath))
            {
                options.ExecutablePath = _executablePath;
            }
            else
            {
                _logger.LogInformation("Ensuring a headless Chromium is available.");
                await new BrowserFetcher().DownloadAsync();
            }

            _browser = await Puppeteer.LaunchAsync(options);
            return _browser;
        }
        catch (Exception ex)
        {
            _unavailable = true;
            _logger.LogWarning(ex, "Headless Chromium is not available; server-side export is disabled.");

            throw new ExportUnavailableException(
                "Server-side export is unavailable: a headless Chromium could not be started. " +
                "Set Export:ChromiumPath to an installed browser, or use the client-side PNG export.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _gate.Dispose();
    }
}
