using DirectService.Transportation.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace DirectService.Transportation.Infrastructure.Printing;

/// <summary>
/// HTML → PDF with headless Chromium (Q-08) through PuppeteerSharp. One browser per process, started on first use and restarted if
/// it dies; pages are rendered one at a time per browser. The browser executable comes from <c>Printing:ChromiumPath</c>; when it is
/// not set, the machine's Chrome or Edge is used (development). Hosting chooses the production browser (DEF-01).
/// </summary>
public sealed class ChromiumPdfRenderer(IConfiguration config, ILogger<ChromiumPdfRenderer> log) : IPdfRenderer, IAsyncDisposable
{
    private static readonly Action<ILogger, Exception?> RenderFailed =
        LoggerMessage.Define(LogLevel.Warning, new EventId(4101, "PdfRenderFailed"), "PDF rendering failed; the browser will be restarted on the next request.");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IBrowser? _browser;

    private static readonly string[] KnownPaths =
    [
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        "/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome",
    ];

    public async Task<byte[]> RenderAsync(string html, PdfFooter footer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(footer);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var browser = await BrowserAsync().ConfigureAwait(false);
            await using var page = await browser.NewPageAsync().ConfigureAwait(false);
            // Defence in depth (review M4): no JavaScript in documents; only data: URIs and the brand web fonts may load, so a
            // document can never make the server fetch anything else (SSRF); without internet the fonts fall back after 4 s.
            await page.SetJavaScriptEnabledAsync(false).ConfigureAwait(false);
            await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);
            page.Request += async (_, e) =>
            {
                var url = e.Request.Url;
                var allowed = url.StartsWith("data:", StringComparison.Ordinal) || url.StartsWith("about:", StringComparison.Ordinal)
                    || url.StartsWith("https://fonts.googleapis.com/", StringComparison.Ordinal) || url.StartsWith("https://fonts.gstatic.com/", StringComparison.Ordinal);
                if (allowed) await e.Request.ContinueAsync().ConfigureAwait(false);
                else await e.Request.AbortAsync().ConfigureAwait(false);
            };
            try
            {
                await page.SetContentAsync(html, new SetContentOptions { WaitUntil = [WaitUntilNavigation.Load], Timeout = 4_000 }).ConfigureAwait(false);
            }
            catch (WaitTaskTimeoutException)
            {
                // Fonts unreachable: render with the fallback fonts.
            }
            catch (NavigationException)
            {
                // Same: the content is set; only the font download did not finish.
            }
            var dir = footer.RightToLeft ? "rtl" : "ltr";
            var footerHtml = $"<div style=\"width:100%;font-size:8px;color:#5B6570;padding:0 12mm;display:flex;justify-content:space-between;direction:{dir};font-family:Segoe UI,Tahoma,sans-serif\">"
                + $"<span>{System.Net.WebUtility.HtmlEncode(footer.LeftText)} · <span class=\"date\"></span></span>"
                + $"<span>{System.Net.WebUtility.HtmlEncode(footer.PageWord)} <span class=\"pageNumber\"></span> {System.Net.WebUtility.HtmlEncode(footer.OfWord)} <span class=\"totalPages\"></span></span></div>";
            // Per-render limit (review L6): a very large annex must not hold the gate indefinitely.
            return await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                PreferCSSPageSize = true,
                DisplayHeaderFooter = true,
                HeaderTemplate = "<span></span>",
                FooterTemplate = footerHtml,
                MarginOptions = new MarginOptions { Top = "14mm", Bottom = "18mm", Left = "12mm", Right = "12mm" },
            }).WaitAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RenderFailed(log, ex);
            await ResetAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IBrowser> BrowserAsync()
    {
        if (_browser is { IsConnected: true }) return _browser;
        var path = config["Printing:ChromiumPath"];
        if (string.IsNullOrWhiteSpace(path)) path = KnownPaths.FirstOrDefault(File.Exists);
        if (path is null) throw new InvalidOperationException("No Chromium-based browser found; set Printing:ChromiumPath.");
        _browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = path,
            Args = ["--no-first-run", "--disable-gpu", "--disable-extensions", "--disable-dev-shm-usage"],
        }).ConfigureAwait(false);
        return _browser;
    }

    private async Task ResetAsync()
    {
        var b = _browser;
        _browser = null;
        if (b is null) return;
        try { await b.CloseAsync().ConfigureAwait(false); } catch (PuppeteerException) { } catch (InvalidOperationException) { }
        await b.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
