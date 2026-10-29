using System.Globalization;
using System.Text;
using static DirectService.SharedKernel.DocumentPrinter;

namespace DirectService.Shared.Infrastructure.Reports;

/// <summary>
/// Export of any report or screen (FR-121, FR-150): Excel (default) or CSV with the same columns, or a printout (PDF, or its HTML) with
/// the company header, title, filters, print date and user, page X of Y, in the reader's direction (RTL for Arabic). Used by the
/// financial reports (B9) and the report engine (B10). Shared since Stage 2 session 8; modules use it through <see cref="IReportExporter"/> (DT-326).
/// </summary>
public sealed class ReportExporter(ICompanyInfo companies, IRequestContext ctx, DocumentPrinter printer, ISpreadsheetService sheets, TimeProvider clock) : IReportExporter
{
    public bool En => ctx.Language == "en";

    public string L(string ar, string en) => En ? en : ar;

    private const int MaxPrintoutRows = IReportExporter.MaxPrintoutRows;

    /// <summary>
    /// The printout (PDF / HTML) of a list screen's export (FR-150): the same rows as its Excel file, with the columns that fit an A4 page
    /// (<paramref name="keep"/>, all when null); value kinds come from the data (amounts, dates, counts).
    /// </summary>
    /// <remarks>
    /// <paramref name="quantities"/>: source columns holding decimals that are not money (weights, rates per ton print as numbers);
    /// <paramref name="sums"/>: source columns totalled when <paramref name="totals"/> is on (default: the money columns).
    /// A printout is refused above <see cref="MaxPrintoutRows"/> rows (Excel has no such limit).
    /// </remarks>
    public Task<GeneratedFile> ListPrintoutAsync(string title, string fileName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, string format,
        CancellationToken ct, IReadOnlyList<int>? keep = null, bool totals = false, IReadOnlyList<(string Label, string Value)>? filters = null,
        IReadOnlyList<int>? quantities = null, IReadOnlyList<int>? sums = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count > MaxPrintoutRows)
            throw new AppException(SharedErrorCodes.PrintoutTooLarge, 422, "format", MaxPrintoutRows.ToString("N0", CultureInfo.InvariantCulture));
        var columns = keep ?? Enumerable.Range(0, headers.Count).ToList();
        ReportValue Kind(int i) => rows.Select(r => i < r.Count ? r[i] : null).FirstOrDefault(v => v is not null) switch
        {
            decimal when quantities?.Contains(i) == true => ReportValue.Number,
            decimal => ReportValue.Money,
            DateOnly or DateTime => ReportValue.Date,
            int or long => ReportValue.Number,
            _ => ReportValue.Text,
        };
        var kinds = columns.Select(Kind).ToList();
        var data = rows.Select(r => (IReadOnlyList<object?>)columns.Select(i => i < r.Count ? r[i] : null).ToList()).ToList();
        // Totals of the amount columns (weights, money) for statements (FR-018).
        bool Summed(int i, int n) => sums?.Contains(i) ?? kinds[n] == ReportValue.Money;
        IReadOnlyList<object?>? sum = totals
            ? columns.Select((i, n) => n == 0 ? L("الإجمالي", "Total") : Summed(i, n) ? data.Sum(r => r[n] is decimal d ? d : 0m) : (object?)null).ToList()
            : null;
        var table = new ReportTable(title, filters ?? [], columns.Select((i, n) => new ReportColumn(headers[i], kinds[n])).ToList(), data, sum);
        return ExportAsync(table, format, fileName, ct);
    }

    public async Task<GeneratedFile> ExportAsync(ReportTable table, string? format, string fileName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(table);
        var f = format?.Trim().ToLowerInvariant();
        var headers = table.Columns.Select(c => c.Header).ToList();
        var rows = table.Totals is null ? table.Rows : [.. table.Rows, table.Totals];
        switch (f)
        {
            case null or "" or "xlsx" or "excel":
                return new GeneratedFile(Safe(fileName) + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    sheets.WriteXlsx(SheetName(table.Title), headers, rows.Select(r => (IReadOnlyList<object?>)r.Select(Cell).ToList())));
            case "csv":
                return new GeneratedFile(Safe(fileName) + ".csv", "text/csv", sheets.WriteCsv(headers, rows.Select(r => (IReadOnlyList<object?>)r.Select(Cell).ToList())));
            case "pdf" or "html":
                var company = await companies.CurrentAsync(ct).ConfigureAwait(false);
                return await printer.OutputAsync(Html(table, company), f, Safe(fileName), ct).ConfigureAwait(false);
            default:
                throw Fail.Field("format", SharedErrorCodes.ValidationFormat, new FieldRef("format"));
        }
    }

    private static object? Cell(object? value) => value is ReportGroupCell g ? g.Text : value;

    private static string SheetName(string title)
    {
        var clean = new string(title.Where(ch => ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray());
        return clean.Length <= 31 ? clean : clean[..31];
    }

    private static string Safe(string name) => new(name.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());

    private string Html(ReportTable t, CompanyHeader company)
    {
        var printed = clock.GetUtcNow().UtcDateTime;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(company.TimeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(printed, tz).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        if (t.Landscape) sb.Append("<style>@page { size: A4 landscape; }</style>");
        sb.Append(printer.Header(company, band: L("طُبع في", "Printed") + " " + local + " · " + (ctx.UserName ?? string.Empty)));
        sb.Append("<h1>").Append(E(t.Title)).Append("</h1>");
        if (t.Filters.Count > 0)
            sb.Append("<div class=\"meta\">").Append(string.Join(" · ", t.Filters.Select(x => E(x.Label) + ": " + N(x.Value)))).Append("</div>");
        if (t.Note is not null) sb.Append("<div class=\"band\">").Append(E(t.Note)).Append("</div>");
        sb.Append("<table><thead><tr>");
        foreach (var c in t.Columns) sb.Append(c.Kind is ReportValue.Money or ReportValue.Number ? "<th style=\"text-align:end\">" : "<th>").Append(E(c.Header)).Append("</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var r in t.Rows) Row(sb, t.Columns, r, false);
        if (t.Totals is not null) Row(sb, t.Columns, t.Totals, true);
        sb.Append("</tbody></table>");
        return printer.Shell(t.Title, sb.ToString(), null);
    }

    private static void Row(StringBuilder sb, IReadOnlyList<ReportColumn> columns, IReadOnlyList<object?> r, bool total)
    {
        if (r.Count == 1 && r[0] is ReportGroupCell g)
        {
            sb.Append("<tr><th colspan=\"").Append(columns.Count).Append("\">").Append(E(g.Text)).Append("</th></tr>");
            return;
        }
        sb.Append(total ? "<tr style=\"font-weight:700\">" : "<tr>");
        for (var i = 0; i < columns.Count; i++)
        {
            var v = i < r.Count ? r[i] : null;
            var text = v switch
            {
                null => string.Empty,
                decimal m when columns[i].Kind == ReportValue.Money => M(m),
                decimal n => n.ToString("#,##0.###", CultureInfo.InvariantCulture),
                int n => n.ToString("#,##0", CultureInfo.InvariantCulture),
                DateOnly d => D(d),
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty,
            };
            sb.Append(columns[i].Kind is ReportValue.Money or ReportValue.Number or ReportValue.Date ? "<td class=\"n\">" + N(text) : "<td>" + E(text)).Append("</td>");
        }
        sb.Append("</tr>");
    }
}
