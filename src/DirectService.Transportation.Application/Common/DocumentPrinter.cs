using System.Globalization;
using System.Net;
using System.Text;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Core;

namespace DirectService.Transportation.Application.Common;

/// <summary>
/// Shared chrome of every printed document (invoices, credit notes, vouchers, statements — UX-22, UX-23): A4 HTML shell in the reader's
/// direction with the brand fonts, company header with logo and tax numbers, encoding helpers, and the output as HTML or as the same
/// HTML rendered to PDF with page X of Y. Callers log the print (<see cref="AuditTrail"/>); it is saved only once the file exists.
/// </summary>
public sealed class DocumentPrinter(IAppDbContext db, IRequestContext ctx, IPdfRenderer pdf)
{
    public bool En => ctx.Language == "en";

    public string L(string ar, string en) => En ? en : ar;

    public static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

    public static string M(decimal v) => v.ToString("#,##0.000", CultureInfo.InvariantCulture);

    public static string D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>A left-to-right run inside RTL text (numbers, codes, dates).</summary>
    public static string N(string? s) => $"<bdi dir=\"ltr\">{E(s)}</bdi>";

    public const string Css = """

        @page { size: A4; margin: 14mm 12mm 18mm; }

        bdi { font-family: Inter, 'Segoe UI', sans-serif; font-variant-numeric: tabular-nums; }
        .hd { display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid #0A3540; padding-bottom: 6px; }
        .hd img { height: 46px; }
        .band { background: #E4EDEE; padding: 4px 8px; margin-top: 6px; font-size: 9.5pt; }
        h1 { font-size: 15pt; text-align: center; margin: 10px 0 2px; }
        h2 { font-size: 11.5pt; margin: 12px 0 2px; }
        .meta { text-align: center; margin-bottom: 8px; }
        .parties { display: flex; justify-content: space-between; gap: 16px; margin: 8px 0; }
        table { width: 100%; border-collapse: collapse; margin-top: 6px; }
        th, td { border: 1px solid #9AA3A1; padding: 3px 5px; text-align: start; vertical-align: top; }
        th { background: #E4EDEE; font-weight: 600; }
        td.n { text-align: end; }
        .totals { width: 260px; margin-inline-start: auto; margin-top: 8px; }
        .totals div { display: flex; justify-content: space-between; padding: 2px 0; }
        .totals .grand { font-weight: 700; border-top: 1px solid #171A1C; }
        .words { margin-top: 6px; }
        .fields { display: grid; grid-template-columns: max-content 1fr; gap: 4px 14px; margin: 10px 0; }
        .fields b { font-weight: 600; }
        .sig { display: flex; justify-content: space-between; margin-top: 34px; gap: 16px; }
        .sig div { flex: 1; border-top: 1px solid #5B6570; padding-top: 4px; text-align: center; }
        .foot { margin-top: 12px; color: #5B6570; font-size: 9pt; }
        .qr svg { width: 88px; height: 88px; }
        .annex { page-break-before: always; }
        .copy { position: absolute; top: 4mm; inset-inline-end: 0; border: 2px solid #5B6570; color: #5B6570; padding: 1px 10px; font-weight: 700; }
        .stamp { position: fixed; top: 38%; left: 18%; transform: rotate(-24deg); font: 700 88pt Inter, sans-serif; color: rgba(180,35,24,.16); border: 8px solid rgba(180,35,24,.16); padding: 0 24px; }
        """;

    /// <summary>The document: title, body, optional diagonal stamp (DRAFT / VOID) and an optional "Copy" mark (FR-090).</summary>
    public string Shell(string title, string body, string? stamp, string? mark = null) =>
        "<!doctype html><html lang=\"" + (En ? "en" : "ar") + "\" dir=\"" + (En ? "ltr" : "rtl") + "\"><head><meta charset=\"utf-8\"><title>" + E(title) + "</title>"
        + "<style>" + BrandFonts.Css + Css + "body { font-family: " + (En ? "Inter" : "'IBM Plex Sans Arabic'") + ", 'Segoe UI', Tahoma, sans-serif; font-size: 10.5pt; color: #171A1C; margin: 0; position: relative; }</style></head><body>"
        + (stamp is null ? string.Empty : "<div class=\"stamp\">" + E(stamp) + "</div>")
        + (mark is null ? string.Empty : "<div class=\"copy\">" + E(mark) + "</div>") + body + "</body></html>";

    /// <summary>Company name, tax and registration numbers, address, phone, logo, optional header band.</summary>
    public string Header(Company company, bool showLogo = true, string? band = null)
    {
        ArgumentNullException.ThrowIfNull(company);
        var logo = showLogo && company.LogoFile is { Length: > 0 } bytes ? $"<img src=\"data:{ImageType(bytes)};base64,{Convert.ToBase64String(bytes)}\" alt=\"\">" : string.Empty;
        return $"<div class=\"hd\"><div><b style=\"font-size:13pt\">{E(En ? company.NameEn ?? company.Name : company.Name)}</b><div>{L("الرقم الضريبي", "Tax no.")} {N(company.TaxNumber)}"
            + $"{(company.CommercialRegNo is null ? string.Empty : $" · {L("السجل التجاري", "CR")} {N(company.CommercialRegNo)}")}</div><div>{E(company.Address)} {N(company.Phone)}</div></div>{logo}</div>"
            + (band is null ? string.Empty : $"<div class=\"band\">{E(band)}</div>");
    }

    /// <summary>Media type of the stored logo from its first bytes (PNG, JPEG, GIF, WebP; SVG text).</summary>
    private static string ImageType(byte[] b) =>
        b.Length > 3 && b[0] == 0x89 && b[1] == 0x50 ? "image/png"
        : b.Length > 2 && b[0] == 0xFF && b[1] == 0xD8 ? "image/jpeg"
        : b.Length > 3 && b[0] == 0x47 && b[1] == 0x49 ? "image/gif"
        : b.Length > 11 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50 ? "image/webp"
        : b.Length > 0 && b[0] == (byte)'<' ? "image/svg+xml" : "image/png";

    /// <summary>Signature boxes.</summary>
    public static string Signatures(IEnumerable<string> labels) =>
        $"<div class=\"sig\">{string.Concat(labels.Select(s => $"<div>{E(s)}</div>"))}</div>";

    /// <summary>HTML or PDF (default); the pending print log is saved only once the file exists (FR-058, FR-090).</summary>
    public async Task<GeneratedFile> OutputAsync(string html, string? format, string name, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(name);
        var safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());
        switch (format?.Trim().ToLowerInvariant())
        {
            case "html":
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return new GeneratedFile(safe + ".html", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
            case null or "" or "pdf":
                byte[] bytes;
                try
                {
                    bytes = await pdf.RenderAsync(html, new PdfFooter(ctx.UserName ?? string.Empty, En ? "Page" : "صفحة", En ? "of" : "من", !En), ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not AppException)
                {
                    throw new AppException(ErrorCodes.PrintUnavailable, 503);
                }
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return new GeneratedFile(safe + ".pdf", "application/pdf", bytes);
            default:
                throw Fail.Field("format", ErrorCodes.ValidationFormat, "format");
        }
    }
}
