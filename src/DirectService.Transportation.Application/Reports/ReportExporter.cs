using System.Globalization;
using System.Text;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using static DirectService.Transportation.Application.Common.DocumentPrinter;

namespace DirectService.Transportation.Application.Reports;

/// <summary>A report column: header, and how its values print (money with the currency decimals, number, date or text).</summary>
public sealed record ReportColumn(string Header, ReportValue Kind = ReportValue.Text);

public enum ReportValue
{
    Text,
    Money,
    Number,
    Date,
}

/// <summary>
/// A report ready for export: title, the filters applied (label → value), columns, rows, an optional totals row and an optional note
/// (e.g. a warning). Rows marked as groups print in bold across the table.
/// </summary>
public sealed record ReportTable(string Title, IReadOnlyList<(string Label, string Value)> Filters, IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<object?>? Totals = null, string? Note = null);

/// <summary>
/// Export of any report or screen (FR-121, FR-150): Excel (default) or CSV with the same columns, or a printout (PDF, or its HTML) with
/// the company header, title, filters, print date and user, page X of Y, in the reader's direction (RTL for Arabic). Used by the
/// financial reports (B9) and the report engine (B10).
/// </summary>
public sealed class ReportExporter(IAppDbContext db, IRequestContext ctx, DocumentPrinter printer, ISpreadsheetService sheets, TimeProvider clock)
{
    public bool En => ctx.Language == "en";

    public string L(string ar, string en) => En ? en : ar;

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
                var company = await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false);
                return await printer.OutputAsync(Html(table, company), f, Safe(fileName), ct).ConfigureAwait(false);
            default:
                throw Fail.Field("format", ErrorCodes.ValidationFormat, "format");
        }
    }

    /// <summary>Group rows: a single text cell shown as a band (Excel: the text in the first column).</summary>
    public static IReadOnlyList<object?> Group(string text) => [new GroupCell(text)];

    private sealed record GroupCell(string Text);

    private static object? Cell(object? value) => value is GroupCell g ? g.Text : value;

    private static string SheetName(string title)
    {
        var clean = new string(title.Where(ch => ch is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray());
        return clean.Length <= 31 ? clean : clean[..31];
    }

    private static string Safe(string name) => new(name.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray());

    private string Html(ReportTable t, Company company)
    {
        var printed = clock.GetUtcNow().UtcDateTime;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(company.TimeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(printed, tz).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
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
        if (r.Count == 1 && r[0] is GroupCell g)
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
