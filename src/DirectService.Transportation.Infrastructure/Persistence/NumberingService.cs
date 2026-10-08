using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Configuration;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Persistence;

/// <summary>
/// Gap-free document numbers (BR-037, BR-038, cfg.NumberSequences business rule): the sequence row is
/// read WITH (UPDLOCK, ROWLOCK) inside the issuing transaction and incremented in it, so concurrent
/// issuers wait for each other and a rolled-back document returns its number. Raw SQL (table hint)
/// — listed in BUILD_STATUS.md.
/// </summary>
public sealed class NumberingService(MasterDbContext db, IDataScope scope) : INumberingService
{
    private sealed record SequenceRow(long Id, string Prefix, byte Digits, long NextNumber);

    public async Task<string> NextAsync(string documentType, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Numbers must be taken inside the document's transaction (BR-038).");
        var companyId = scope.CompanyId ?? throw new InvalidOperationException("No company in scope.");

        var row = await db.Database.SqlQuery<SequenceRow>($"""
            SELECT Id, Prefix, Digits, NextNumber
            FROM cfg.NumberSequences WITH (UPDLOCK, ROWLOCK)
            WHERE CompanyId = {companyId} AND DocumentType = {documentType} AND IsDeleted = 0
            """).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Number sequence {documentType} is missing for company {companyId}.");

        await db.Set<Domain.Cfg.NumberSequence>().IgnoreQueryFilters()
            .Where(s => s.Id == row.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextNumber, x => x.NextNumber + 1), ct)
            .ConfigureAwait(false);

        return DocumentTypes.Format(row.Prefix, row.Digits, row.NextNumber);
    }
}
