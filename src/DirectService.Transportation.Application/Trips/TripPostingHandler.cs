using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Trips;

/// <summary>
/// Pending-posting retries and journal links for trips (FR-101): rebuilds the wage / partner-cost entry from the stored trip,
/// attaches the posted entry, and describes trips by receipt number. A trip that is void or already has its entry needs none.
/// </summary>
public sealed class TripPostingHandler(IAppDbContext db, TripService trips, IUrlTokenService tokens) : IPostingSourceHandler
{
    public string SourceModule => AccountingCodes.ModuleTrn;
    public string SourceType => TripService.SourceType;

    public async Task<PostingRequest?> BuildAsync(long sourceId, CancellationToken ct)
    {
        var t = await db.Set<Trip>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        if (t is null || t.JournalEntryId is not null) return null;
        var s = await trips.SettingsAsync(ct).ConfigureAwait(false);
        return trips.BuildEntry(t, s.AccrueDriverWage);
    }

    public async Task AttachAsync(long sourceId, JournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var t = await db.Set<Trip>().SingleAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        // A retry racing an edit or a void must not leave a second entry or an entry on a void trip (review M1): refusing here
        // rolls the retry back and leaves the pending row for the next attempt, which then finds nothing to post.
        if (t.Status == TripService.Void) throw Fail.Rule(ErrorCodes.TripIsVoid, t.ReceiptNo);
        if (t.JournalEntryId is not null) throw new AppException(ErrorCodes.ConcurrencyConflict, 409);
        t.JournalEntryId = entry.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<long, SourceDocumentRef>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
        (await db.Set<Trip>().AsNoTracking().Where(t => sourceIds.Contains(t.Id)).Select(t => new { t.Id, t.PublicId, t.ReceiptNo }).ToListAsync(ct).ConfigureAwait(false))
        .ToDictionary(t => t.Id, t => new SourceDocumentRef(SourceModule, SourceType, tokens.Encode<Trip>(t.PublicId), t.ReceiptNo));
}
