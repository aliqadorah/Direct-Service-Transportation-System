using DirectService.Transportation.Application.Common;

namespace DirectService.Transportation.Application.Abstractions;

/// <summary>
/// Reads and writes tabular files (ADR-13): .xlsx (first worksheet) and CSV. Reading takes cell values only and
/// refuses formula cells (SEC-13); size and row limits are checked before parsing.
/// </summary>
public interface ISpreadsheetService
{
    /// <summary>Reads the header row and the data rows. Throws a 400/422 <see cref="AppException"/> for bad files.</summary>
    SheetData Read(Stream content, string fileName, long length);

    byte[] WriteXlsx(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows);

    byte[] WriteCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows);
}

/// <summary>Tabular file contents; row numbers are those of the sheet (header = row 1).</summary>
public sealed record SheetData(IReadOnlyList<string> Headers, IReadOnlyList<SheetRow> Rows)
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int MaxRows = 20_000;

    /// <summary>
    /// Index of the first header matching any alias (Arabic or English, case/space-insensitive, Arabic digits folded),
    /// or −1.
    /// </summary>
    public int Column(params string[] aliases)
    {
        var keys = aliases.Select(TextNormalizer.Key).ToHashSet();
        for (var i = 0; i < Headers.Count; i++)
        {
            if (keys.Contains(TextNormalizer.Key(Headers[i]))) return i;
        }
        return -1;
    }
}

public sealed record SheetRow(int RowNumber, IReadOnlyList<string?> Cells)
{
    public string? this[int column] => column >= 0 && column < Cells.Count ? Cells[column] : null;
}
