using System.Reflection;
using System.Text;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Infrastructure.Printing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PuppeteerSharp;

namespace DirectService.Transportation.Infrastructure.Tests;

/// <summary>
/// The PDF engine (DT-222, owner testing 20 Oct 2026 item 2): the invoice print failed now and then in a full test run with "print engine not
/// available" (503). Under memory pressure the headless browser — or its page — dies between or during prints; the engine noticed only
/// after the failed print and answered that print with 503. It now renders again once on a fresh browser.
/// </summary>
public sealed class PdfRendererTests
{
    private static readonly PdfFooter Footer = new("Test", "Page", "of", false);

    private static ChromiumPdfRenderer Renderer() =>
        new(new ConfigurationBuilder().AddEnvironmentVariables().Build(), NullLogger<ChromiumPdfRenderer>.Instance);   // CI: Printing__NoSandbox

    private static IBrowser? Browser(ChromiumPdfRenderer r) =>
        (IBrowser?)typeof(ChromiumPdfRenderer).GetField("_browser", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(r);

    private static void AssertPdf(byte[] bytes) => Assert.StartsWith("%PDF", Encoding.ASCII.GetString(bytes[..4]), StringComparison.Ordinal);

    [Fact]
    public async Task A_print_after_the_browser_died_is_rendered_on_a_new_browser()
    {
        await using var r = Renderer();
        AssertPdf(await r.RenderAsync("<p>first</p>", Footer, TestContext.Current.CancellationToken));
        var process = Browser(r)!.Process!;
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        AssertPdf(await r.RenderAsync("<p>second</p>", Footer, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_browser_that_dies_during_a_print_does_not_fail_the_print()
    {
        await using var r = Renderer();
        var killed = 0;
        r.AfterPageOpened = async _ =>
        {
            if (Interlocked.Increment(ref killed) > 1) return;   // only the first attempt
            var process = Browser(r)!.Process!;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        };
        AssertPdf(await r.RenderAsync("<p>while the browser dies</p>", Footer, TestContext.Current.CancellationToken));
        Assert.Equal(2, killed);   // the print was rendered again on a new browser
    }

    [Fact]
    public async Task A_browser_that_keeps_dying_still_ends_in_an_error_not_a_hang()
    {
        await using var r = Renderer();
        r.AfterPageOpened = async _ =>
        {
            var process = Browser(r)!.Process!;
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        };
        await Assert.ThrowsAnyAsync<Exception>(() => r.RenderAsync("<p>x</p>", Footer, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Prints_queued_at_the_same_time_all_succeed()
    {
        await using var r = Renderer();
        var all = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => r.RenderAsync($"<p>doc {i}</p>", Footer, TestContext.Current.CancellationToken)));
        Assert.All(all, AssertPdf);
    }
}
