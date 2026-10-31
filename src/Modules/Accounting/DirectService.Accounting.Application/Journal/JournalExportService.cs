using DirectService.Platform.Contracts.Errors;
using DirectService.Accounting.Application.Posting;
using DirectService.Accounting.Application.Books;
using System.Text.Json;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Accounting.Application.Journal;

/// <summary>
/// Journal export (acc.je.export; FR-104): the entries of a period range as a JSON or CSV batch for external accounting
/// tools. Each batch takes a number (JEX-, D-16) and is logged in acc.JournalExportBatches (user, time, periods, count,
/// totals); the entries point to their latest batch. Re-export is allowed and makes a new batch.
/// </summary>
public sealed class JournalExportService(IAppDbContext db, INumberingService numbering, JournalService journal, ISpreadsheetService sheets,
    IUrlTokenService tokens, IRequestContext ctx, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<JournalExportBatchResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<JournalExportBatch>().AsNoTracking().OrderByDescending(b => b.Id).Take(500)
            .Select(b => new { b.PublicId, b.BatchNo, b.PeriodFrom, b.PeriodTo, b.Format, b.EntryCount, b.TotalDebit, b.TotalCredit,
                ExportedBy = b.ExportedByUser.FullName, b.ExportedAt })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(b => new JournalExportBatchResponse(tokens.Encode<JournalExportBatch>(b.PublicId), b.BatchNo, b.PeriodFrom, b.PeriodTo,
            b.Format, b.EntryCount, b.TotalDebit, b.TotalCredit, b.ExportedBy, b.ExportedAt)).ToList();
    }

    public async Task<GeneratedFile> ExportAsync(JournalExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        // Allowed for suspended tenants (export stays available), but a read-only support session never writes (D-13, SR-B2-05).
        if (ctx.IsSupportSession) throw new AppException(PlatformErrorCodes.SupportReadOnly, 403);
        var format = r.Format?.ToUpperInvariant();
        new Validator()
            .Check(AccountingCodes.IsMonth(r.PeriodFrom), "periodFrom")
            .Check(AccountingCodes.IsMonth(r.PeriodTo), "periodTo")
            .Check(format is "JSON" or "CSV", "format")
            .ThrowIfInvalid();
        if (string.CompareOrdinal(r.PeriodFrom, r.PeriodTo) > 0) throw Fail.Field("periodTo", SharedErrorCodes.ValidationRange, new FieldRef("periodTo"));

        var entries = await journal.ExportRowsAsync(new JournalQuery(r.PeriodFrom, r.PeriodTo, null, null, null, null, null, null, null, null),
            200_000, ct).ConfigureAwait(false);
        var totalDebit = entries.Sum(e => e.TotalDebit);
        var totalCredit = entries.Sum(e => e.TotalCredit);
        var publicIds = entries.Select(e => tokens.Decode<JournalEntry>(e.Id)!.Value).ToList();

        return await db.InTransactionAsync(async c =>
        {
            var batchNo = await numbering.NextAsync(AccountingDocumentTypes.JournalExport, c).ConfigureAwait(false);
            var batch = new JournalExportBatch
            {
                BatchNo = batchNo, PeriodFrom = r.PeriodFrom!, PeriodTo = r.PeriodTo!, Format = format!, EntryCount = entries.Count,
                TotalDebit = totalDebit, TotalCredit = totalCredit, ExportedBy = ctx.UserId!.Value, ExportedAt = clock.GetUtcNow().UtcDateTime,
            };
            db.Set<JournalExportBatch>().Add(batch);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            foreach (var chunk in publicIds.Chunk(1000))
            {
                await db.Set<JournalEntry>().Where(e => chunk.Contains(e.PublicId))
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.ExportBatchId, batch.Id), c).ConfigureAwait(false);
            }
            var content = format == "JSON" ? JsonFile(batch, entries) : CsvFile(batchNo, entries);
            return new GeneratedFile($"journal-{batchNo}.{format!.ToLowerInvariant()}", format == "JSON" ? "application/json" : "text/csv", content);
        }, ct).ConfigureAwait(false);
    }

    private static byte[] JsonFile(JournalExportBatch batch, IReadOnlyList<JournalEntryResponse> entries) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            batchNo = batch.BatchNo,
            periodFrom = batch.PeriodFrom,
            periodTo = batch.PeriodTo,
            exportedAt = batch.ExportedAt,
            entryCount = batch.EntryCount,
            totalDebit = batch.TotalDebit,
            totalCredit = batch.TotalCredit,
            entries = entries.Select(e => new
            {
                number = e.Number,
                date = e.EntryDate,
                period = e.PeriodMonth,
                description = e.Description,
                reference = e.Reference,
                sourceModule = e.SourceModule,
                sourceType = e.SourceType,
                sourceNumber = e.Source?.Number,
                isManual = e.IsManual,
                status = e.Status,
                reverses = e.ReversesEntry?.Number,
                reversedBy = e.ReversedByEntry?.Number,
                totalDebit = e.TotalDebit,
                totalCredit = e.TotalCredit,
                lines = e.Lines.Select(l => new
                {
                    lineNo = l.LineNo,
                    accountCode = l.Account.Code,
                    accountName = l.Account.NameAr,
                    debit = l.Debit,
                    credit = l.Credit,
                    costCentre = l.CostCentre?.Name,
                    partyType = l.Party?.Type,
                    partyName = l.Party?.Name,
                    note = l.Note,
                }),
            }),
        }, Json);

    private byte[] CsvFile(string batchNo, IReadOnlyList<JournalEntryResponse> entries)
    {
        string[] headers =
        [
            "BatchNo", "EntryNumber", "EntryDate", "Period", "Description", "Reference", "SourceModule", "SourceType", "SourceNumber", "IsManual", "Status",
            "Reverses", "LineNo", "AccountCode", "AccountName", "Debit", "Credit", "CostCentre", "PartyType", "PartyName", "Note",
        ];
        var rows = entries.SelectMany(e => e.Lines.Select(l => (IReadOnlyList<object?>)
        [
            batchNo, e.Number, e.EntryDate, e.PeriodMonth, e.Description, e.Reference, e.SourceModule, e.SourceType, e.Source?.Number, e.IsManual, e.Status,
            e.ReversesEntry?.Number, l.LineNo, l.Account.Code, l.Account.NameAr, l.Debit, l.Credit, l.CostCentre?.Name, l.Party?.Type, l.Party?.Name,
            l.Note,
        ]));
        return sheets.WriteCsv(headers, rows);
    }
}
