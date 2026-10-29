using DirectService.Shared.Contracts;

namespace DirectService.SharedKernel;

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
/// (e.g. a warning). Rows marked as groups print in bold across the table. <see cref="Landscape"/>: the printout is A4 landscape (many columns).
/// </summary>
public sealed record ReportTable(string Title, IReadOnlyList<(string Label, string Value)> Filters, IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<object?>? Totals = null, string? Note = null)
{
    public bool Landscape { get; init; }
}

/// <summary>The single cell of a group row (<see cref="IReportExporter.Group"/>): a band across the printout, the text in the first column in Excel.</summary>
public sealed record ReportGroupCell(string Text);

/// <summary>
/// Export of any report or screen (FR-121, FR-150): Excel (default) or CSV with the same columns, or a printout (PDF, or its HTML) with
/// the company header, title, filters, print date and user, page X of Y, in the reader's direction (RTL for Arabic). Implemented by the
/// shared <c>ReportExporter</c> (Stage 2 session 8, DT-326).
/// </summary>
public interface IReportExporter
{
    /// <summary>Longest list a PDF/HTML printout renders (larger lists go to Excel or a background export).</summary>
    const int MaxPrintoutRows = 5000;

    /// <summary>A list export asked as a printout ("pdf" or "html")? Excel and CSV stay with the list's own writer.</summary>
    static bool IsPrintout(string? format) => format?.Trim().ToLowerInvariant() is "pdf" or "html";

    /// <summary>Group rows: a single text cell shown as a band (Excel: the text in the first column).</summary>
    static IReadOnlyList<object?> Group(string text) => [new ReportGroupCell(text)];

    bool En { get; }

    string L(string ar, string en);

    /// <summary>
    /// The printout (PDF / HTML) of a list screen's export (FR-150): the same rows as its Excel file, with the columns that fit an A4 page
    /// (<paramref name="keep"/>, all when null); value kinds come from the data (amounts, dates, counts).
    /// </summary>
    /// <remarks>
    /// <paramref name="quantities"/>: source columns holding decimals that are not money (weights, rates per ton print as numbers);
    /// <paramref name="sums"/>: source columns totalled when <paramref name="totals"/> is on (default: the money columns).
    /// A printout is refused above <see cref="MaxPrintoutRows"/> rows (Excel has no such limit).
    /// </remarks>
    Task<GeneratedFile> ListPrintoutAsync(string title, string fileName, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows, string format,
        CancellationToken ct, IReadOnlyList<int>? keep = null, bool totals = false, IReadOnlyList<(string Label, string Value)>? filters = null,
        IReadOnlyList<int>? quantities = null, IReadOnlyList<int>? sums = null);

    Task<GeneratedFile> ExportAsync(ReportTable table, string? format, string fileName, CancellationToken ct);
}
