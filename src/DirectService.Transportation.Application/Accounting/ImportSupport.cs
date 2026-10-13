using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Cfg;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>Shared parsing and problem collection for the accounting imports (strict mode: any rejected row blocks the import).</summary>
internal sealed class ImportProblems(string language)
{
    private readonly SortedDictionary<int, (string? Key, List<ImportProblem> Problems)> _rows = [];

    public void Add(int row, string? key, string code, params object?[] args) => AddAt(row, key, null, code, null, args);

    /// <summary>
    /// A problem of a row in a column of the file (null = the whole row), with its fix: <paramref name="fix"/> when given (e.g.
    /// <see cref="ImportFixCatalog.Date"/>), otherwise the fix of the code.
    /// </summary>
    public void AddAt(int row, string? key, string? column, string code, string? fix, params object?[] args)
    {
        if (!_rows.TryGetValue(row, out var entry))
        {
            entry = (key, []);
            _rows[row] = entry;
        }
        entry.Problems.Add(new ImportProblem(code, column, ErrorCatalog.Format(code, language, args), ImportFixCatalog.For(fix ?? code, language)));
    }

    public bool Has(int row) => _rows.ContainsKey(row);

    public int RejectedRows => _rows.Count;

    public ImportPreview Preview(IReadOnlyList<(int Row, string? Key)> allRows) =>
        new(allRows.Count, allRows.Count - _rows.Count, _rows.Count,
            allRows.Select(r => _rows.TryGetValue(r.Row, out var p)
                ? new ImportRowResult(r.Row, r.Key, p.Problems.Select(x => x.Message).ToList(), p.Problems)
                : new ImportRowResult(r.Row, r.Key, [])).ToList());
}

internal static class ImportParse
{
    /// <summary>Amount cell: empty = 0; Arabic digits and thousands separators accepted.</summary>
    public static bool TryAmount(string? text, int decimals, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;
        var clean = TextNormalizer.ToAsciiDigits(text).Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("٬", string.Empty, StringComparison.Ordinal).Replace("٫", ".", StringComparison.Ordinal).Trim();
        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && Money.IsValidAmount(value, decimals);
    }

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "d/M/yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "d-M-yyyy"];

    public static bool TryDate(string? text, out DateOnly value) =>
        DateOnly.TryParseExact(TextNormalizer.ToAsciiDigits(text).Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    /// <summary>Yes/no cell: true/false, yes/no, 1/0, نعم/لا; null when empty or unknown.</summary>
    public static bool? Bool(string? text) => TextNormalizer.Key(text) switch
    {
        "TRUE" or "YES" or "1" or "Y" or "نعم" => true,
        "FALSE" or "NO" or "0" or "N" or "لا" => false,
        _ => null,
    };

    public static ImportBatch Batch(string entityType, string fileName, int total, int imported, long userId, string mode = "Strict") => new()
    {
        EntityType = entityType,
        FileName = fileName.Length <= 200 ? fileName : fileName[^200..],
        Mode = mode,
        TotalRows = total,
        ImportedRows = imported,
        RejectedRows = total - imported,
        ImportedBy = userId,
        ImportedAt = DateTime.UtcNow,
    };

    public static void RequireColumns(SheetData sheet, params (int Index, string Name)[] columns)
    {
        foreach (var (index, name) in columns)
        {
            if (index < 0) throw Fail.Rule(ErrorCodes.ImportMissingColumn, name);
        }
    }
}
