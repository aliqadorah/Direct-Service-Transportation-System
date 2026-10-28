using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace DirectService.Shared.Infrastructure.Files;

/// <summary>ClosedXML (.xlsx) and CsvHelper (CSV) implementation of <see cref="ISpreadsheetService"/> (ADR-13).</summary>
public sealed class SpreadsheetService : ISpreadsheetService
{
    public SheetData Read(Stream content, string fileName, long length)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (length > SheetData.MaxBytes) throw new AppException(SharedErrorCodes.ImportTooLarge, 400, "file", "10 MB");
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var data = extension switch
        {
            ".xlsx" => ReadXlsx(content),
            ".csv" or ".txt" or ".tsv" => ReadCsv(content),
            _ => throw new AppException(SharedErrorCodes.ImportFileType, 400, "file"),
        };
        if (data.Rows.Count == 0) throw new AppException(SharedErrorCodes.ImportEmpty, 422, "file");
        if (data.Rows.Count > SheetData.MaxRows)
            throw new AppException(SharedErrorCodes.ImportTooLarge, 422, "file", SheetData.MaxRows.ToString("N0", CultureInfo.InvariantCulture));
        return data;
    }

    /// <summary>Limits on the unpacked .xlsx package (zip-bomb protection, SR-B2-02).</summary>
    private const long MaxUnpackedBytes = 60 * 1024 * 1024;
    private const int MaxColumns = 60;

    private static SheetData ReadXlsx(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        CheckPackage(buffer);
        buffer.Position = 0;
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(buffer);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new AppException(SharedErrorCodes.ImportFileType, 400, "file");
        }
        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new AppException(SharedErrorCodes.ImportEmpty, 422, "file");
            var used = sheet.RangeUsed();
            if (used is null) throw new AppException(SharedErrorCodes.ImportEmpty, 422, "file");
            var lastColumn = used.LastColumn().ColumnNumber();
            if (lastColumn > MaxColumns) throw new AppException(SharedErrorCodes.ImportTooLarge, 422, "file", $"{MaxColumns} columns");
            var lastRow = used.LastRow().RowNumber();
            if (lastRow - 1 > SheetData.MaxRows)
                throw new AppException(SharedErrorCodes.ImportTooLarge, 422, "file", SheetData.MaxRows.ToString("N0", CultureInfo.InvariantCulture));
            var headers = Enumerable.Range(1, lastColumn).Select(c => Text(sheet.Cell(1, c)) ?? string.Empty).ToList();
            var rows = new List<SheetRow>();
            for (var r = 2; r <= lastRow; r++)
            {
                var cells = new List<string?>(lastColumn);
                for (var c = 1; c <= lastColumn; c++)
                {
                    var cell = sheet.Cell(r, c);
                    if (cell.HasFormula) throw new AppException(SharedErrorCodes.ImportFormula, 422, "file", cell.Address.ToString());
                    cells.Add(Text(cell));
                }
                if (cells.Any(v => !string.IsNullOrWhiteSpace(v))) rows.Add(new SheetRow(r, cells));
            }
            return new SheetData(headers, rows);
        }
    }

    /// <summary>Refuses packages whose unpacked size or compression ratio is excessive, before parsing them.</summary>
    private static void CheckPackage(MemoryStream buffer)
    {
        buffer.Position = 0;
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                var ratio = entry.CompressedLength == 0 ? 0 : entry.Length / entry.CompressedLength;
                if (total > MaxUnpackedBytes || (entry.Length > 1024 * 1024 && ratio > 200))
                    throw new AppException(SharedErrorCodes.ImportTooLarge, 422, "file", "10 MB");
            }
        }
        catch (InvalidDataException)
        {
            throw new AppException(SharedErrorCodes.ImportFileType, 400, "file");
        }
    }

    private static string? Text(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        var value = cell.Value;
        if (value.IsNumber) return value.GetNumber().ToString("0.############", CultureInfo.InvariantCulture);
        if (value.IsDateTime) return value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (value.IsBoolean) return value.GetBoolean() ? "true" : "false";
        var text = value.ToString(CultureInfo.InvariantCulture).Trim();
        return text.Length == 0 ? null : text;
    }

    private static SheetData ReadCsv(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true, DetectDelimiter = true, BadDataFound = null };
        using var csv = new CsvReader(reader, config);
        if (!csv.Read() || !csv.ReadHeader()) throw new AppException(SharedErrorCodes.ImportEmpty, 422, "file");
        var headers = csv.HeaderRecord?.Select(h => h.Trim()).ToList() ?? [];
        var rows = new List<SheetRow>();
        var rowNumber = 1;
        while (csv.Read())
        {
            rowNumber++;
            if (rows.Count >= SheetData.MaxRows)
                throw new AppException(SharedErrorCodes.ImportTooLarge, 422, "file", SheetData.MaxRows.ToString("N0", CultureInfo.InvariantCulture));
            var cells = Enumerable.Range(0, headers.Count).Select(i =>
            {
                var v = csv.GetField(i)?.Trim();
                return string.IsNullOrEmpty(v) ? null : v;
            }).ToList();
            // Values only (SEC-13): a leading = or @, or + / - not followed by a number, is a formula in spreadsheet programs.
            var formula = cells.FindIndex(IsFormula);
            if (formula >= 0) throw new AppException(SharedErrorCodes.ImportFormula, 422, "file", $"{headers[formula]}{rowNumber}");
            if (cells.Any(v => v is not null)) rows.Add(new SheetRow(rowNumber, cells));
        }
        return new SheetData(headers, rows);
    }

    private static bool IsFormula(string? v) =>
        v is { Length: > 0 } && (v[0] is '=' or '@'
            || (v[0] is '+' or '-' && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)));

    public byte[] WriteXlsx(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var c = 0; c < headers.Count; c++) sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.000"; break;
                    case int i: cell.Value = i; break;
                    case long l: cell.Value = l; break;
                    case bool b: cell.Value = b; break;
                    case DateOnly date: cell.Value = date.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                    // Text is written as text, so a value starting with = never becomes a formula (CSV/formula injection).
                    default: cell.SetValue(Convert.ToString(row[c], CultureInfo.InvariantCulture)); break;
                }
            }
            r++;
        }
        sheet.Columns().AdjustToContents(1, Math.Min(r, 200));
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] WriteTemplate(SpreadsheetTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        using var workbook = new XLWorkbook();
        var data = workbook.Worksheets.Add(template.DataSheet);
        data.RightToLeft = template.RightToLeft;
        for (var c = 0; c < template.Headers.Count; c++)
        {
            var cell = data.Cell(1, c + 1);
            cell.SetValue(template.Headers[c]);
            cell.Style.Font.Bold = true;
            // Required columns stand out; the header text stays exactly what the import matches (no marks added to it).
            cell.Style.Fill.BackgroundColor = template.Required.Count > c && template.Required[c] ? XLColor.FromHtml("#FCE8C3") : XLColor.FromHtml("#E8EEF4");
            // Text columns are formatted as text so long numbers (national IDs, receipt numbers) keep their digits.
            data.Column(c + 1).Style.NumberFormat.Format = "@";
        }
        var r = 2;
        foreach (var row in template.SampleRows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                var cell = data.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "0.000"; break;
                    case int i: cell.Value = i; cell.Style.NumberFormat.Format = "0"; break;
                    case DateOnly date: cell.Value = date.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                    default: cell.SetValue(Convert.ToString(row[c], CultureInfo.InvariantCulture)); break;
                }
            }
            r++;
        }
        data.SheetView.FreezeRows(1);
        data.Columns(1, Math.Max(1, template.Headers.Count)).AdjustToContents(1, r);
        foreach (var column in data.Columns(1, Math.Max(1, template.Headers.Count))) column.Width = Math.Max(column.Width, 14);

        var notes = workbook.Worksheets.Add(template.NotesSheet);
        notes.RightToLeft = template.RightToLeft;
        notes.Cell(1, 1).SetValue(template.Intro);
        notes.Cell(1, 1).Style.Alignment.WrapText = true;
        notes.Range(1, 1, 1, Math.Max(1, template.NotesHeaders.Count)).Merge();
        notes.Row(1).Height = 48;
        for (var c = 0; c < template.NotesHeaders.Count; c++)
        {
            var cell = notes.Cell(3, c + 1);
            cell.SetValue(template.NotesHeaders[c]);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF4");
        }
        for (var i = 0; i < template.Notes.Count; i++)
        {
            for (var c = 0; c < template.Notes[i].Count; c++)
            {
                var cell = notes.Cell(i + 4, c + 1);
                cell.SetValue(template.Notes[i][c]);
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            }
        }
        notes.Column(1).Width = 28;
        notes.Column(2).Width = 12;
        notes.Column(3).Width = 48;
        notes.Column(4).Width = 60;
        data.SetTabActive();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public byte[] WriteCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);
        using var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
        {
            foreach (var h in headers) csv.WriteField(h);
            csv.NextRecord();
            foreach (var row in rows)
            {
                foreach (var v in row) csv.WriteField(Field(v));
                csv.NextRecord();
            }
        }
        return ms.ToArray();
    }

    /// <summary>CSV cell text; text starting with = + - @ is prefixed with ' so spreadsheet programs do not run it (CSV injection).</summary>
    private static string Field(object? v) => v switch
    {
        null => string.Empty,
        decimal d => d.ToString("0.000", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f when v is not string => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Neutralise(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty),
    };

    private static string Neutralise(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;
}
