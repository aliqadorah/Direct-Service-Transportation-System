namespace DirectService.SharedKernel;

/// <summary>Page footer text of a printed document (page X of Y is added by the renderer, UX-22).</summary>
public sealed record PdfFooter(string LeftText, string PageWord, string OfWord, bool RightToLeft);

/// <summary>
/// HTML → PDF (Q-08: HTML + headless Chromium). The PDF is identical to the printout (UX-23): A4, the document's own CSS, page
/// X of Y in the footer. Provider-neutral; the development implementation drives the machine's Chrome/Edge.
/// </summary>
public interface IPdfRenderer
{
    Task<byte[]> RenderAsync(string html, PdfFooter footer, CancellationToken ct);
}

/// <summary>QR codes printed on documents (FR-057, FR-062) as inline SVG.</summary>
public interface IQrCodeRenderer
{
    string Svg(string content);
}
