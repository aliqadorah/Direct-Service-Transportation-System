using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Accounting.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// A driver's or partner's previous balance (owner findings 27 Oct 2026, DT-276 / DT-277): the lines of manual journal entries (with their
/// reversals) and of the opening entry that name the party (DT-269: credit = owed to the party, debit = owed by it), less what active
/// settlements / partner payments already settled (trn.TRN_PreviousBalanceItems), per journal entry and account. A settlement or payment
/// settles the open amounts up to its date; its entry clears them on the same accounts with the same party (<see cref="Lines"/>), so the
/// control account is back to zero for the party in RPT-23 and the statements (RPT-13 / RPT-14) close at zero. Voiding the document makes its
/// items inactive again.
/// </summary>
public sealed class PreviousBalances(IAppDbContext db, MasterDataSupport support)
{
    /// <summary>One open amount: of a manual / opening entry, on one account (signed: above zero owed to the party).</summary>
    public sealed record Open(long JournalEntryId, long AccountId, decimal Amount);

    /// <summary>Lines of manual entries (with their reversals) and of the opening entry that name a party of the type, up to a date (DT-269).</summary>
    internal static IQueryable<EntryLine> PartyLines(IAppDbContext db, string partyType, DateOnly upTo) =>
        db.Set<EntryLine>().AsNoTracking().Where(l => l.PartyType == partyType && l.PartyId != null && l.JournalEntry.EntryDate <= upTo
            && (l.JournalEntry.IsManual || l.JournalEntry.SourceType == AccountingCodes.SourceOpening));

    /// <summary>The items of the party's active settlements (drivers) or partner payments / repayments (partners).</summary>
    private IQueryable<PreviousBalanceItem> Settled(string partyType, long partyId) => partyType == AccountingCodes.PartyDriver
        ? db.Set<PreviousBalanceItem>().AsNoTracking().Where(i => i.Settlement != null && i.Settlement.DriverId == partyId && i.Settlement.Status == CashCodes.Active)
        : db.Set<PreviousBalanceItem>().AsNoTracking().Where(i => i.PartnerPayment != null && i.PartnerPayment.PartnerId == partyId && i.PartnerPayment.Status == CashCodes.Active);

    /// <summary>
    /// What is still open for the party up to <paramref name="upTo"/>, per journal entry and account (zero amounts left out). A voided or edited
    /// entry and its reversal cancel out, so only entries in force count (posted, not a reversal); what a document already settled of a voided
    /// entry is then open the other way (e.g. owed back to the driver).
    /// </summary>
    public async Task<List<Open>> OpenAsync(string partyType, long partyId, DateOnly upTo, CancellationToken ct)
    {
        var lines = await PartyLines(db, partyType, upTo).Where(l => l.PartyId == partyId && l.JournalEntry.Status == AccountingCodes.Posted && l.JournalEntry.ReversesEntryId == null)
            .GroupBy(l => new { l.JournalEntryId, l.AccountId }).Select(g => new { g.Key.JournalEntryId, g.Key.AccountId, A = g.Sum(l => l.Credit - l.Debit) })
            .ToListAsync(ct).ConfigureAwait(false);
        var settled = await Settled(partyType, partyId).Where(i => db.Set<JournalEntry>().Any(j => j.Id == i.JournalEntryId && j.EntryDate <= upTo))
            .GroupBy(i => new { i.JournalEntryId, i.AccountId }).Select(g => new { g.Key.JournalEntryId, g.Key.AccountId, A = g.Sum(i => i.Amount) })
            .ToListAsync(ct).ConfigureAwait(false);
        return lines.Select(l => new Open(l.JournalEntryId, l.AccountId, l.A))
            .Concat(settled.Select(s => new Open(s.JournalEntryId, s.AccountId, -s.A)))
            .GroupBy(o => (o.JournalEntryId, o.AccountId)).Select(g => new Open(g.Key.JournalEntryId, g.Key.AccountId, g.Sum(o => o.Amount)))
            .Where(o => o.Amount != 0).OrderBy(o => o.JournalEntryId).ThenBy(o => o.AccountId).ToList();
    }

    /// <summary>
    /// The open amounts that settle <paramref name="target"/> (signed, at most the open total in size, DT-277): everything of the other sign
    /// first, then amounts of the target's sign oldest first, the last one in part.
    /// </summary>
    public static List<Open> Take(IReadOnlyList<Open> open, decimal target)
    {
        ArgumentNullException.ThrowIfNull(open);
        if (target == 0) return [];
        var sign = Math.Sign(target);
        var result = open.Where(o => Math.Sign(o.Amount) != sign).ToList();
        var room = Math.Abs(target) + result.Sum(o => Math.Abs(o.Amount));
        foreach (var o in open.Where(o => Math.Sign(o.Amount) == sign))
        {
            if (room <= 0) break;
            var part = Math.Min(Math.Abs(o.Amount), room);
            result.Add(o with { Amount = sign * part });
            room -= part;
        }
        return result;
    }

    /// <summary>The entries behind open amounts or a document's items, oldest first, with the amount of each (all its accounts together).</summary>
    public async Task<List<PreviousBalanceEntry>> EntriesAsync(IEnumerable<Open> amounts, CancellationToken ct)
    {
        var byEntry = amounts.GroupBy(a => a.JournalEntryId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        var ids = byEntry.Keys.ToList();
        var rows = await db.Set<JournalEntry>().AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.PublicId, e.Number, e.EntryDate, e.Description }).ToListAsync(ct).ConfigureAwait(false);
        return rows.OrderBy(e => e.EntryDate).ThenBy(e => e.Id)
            .Select(e => new PreviousBalanceEntry(new DocRef(support.Token<JournalEntry>(e.PublicId), e.Number), e.EntryDate, e.Description, byEntry[e.Id])).ToList();
    }

    /// <summary>The items a settlement or partner payment settled, as open amounts (for its entry and its details).</summary>
    public async Task<List<Open>> OfDocumentAsync(long? settlementId, long? partnerPaymentId, CancellationToken ct) =>
        await db.Set<PreviousBalanceItem>().AsNoTracking()
            .Where(i => settlementId != null ? i.SettlementId == settlementId : i.PartnerPaymentId == partnerPaymentId)
            .OrderBy(i => i.Id).Select(i => new Open(i.JournalEntryId, i.AccountId, i.Amount)).ToListAsync(ct).ConfigureAwait(false);

    /// <summary>Stores what a document settles (one item per entry and account).</summary>
    public void Add(IEnumerable<Open> amounts, long? settlementId, long? partnerPaymentId)
    {
        foreach (var a in amounts)
            db.Set<PreviousBalanceItem>().Add(new PreviousBalanceItem
            {
                SettlementId = settlementId, PartnerPaymentId = partnerPaymentId, JournalEntryId = a.JournalEntryId, AccountId = a.AccountId, Amount = a.Amount,
            });
    }

    /// <summary>
    /// Entry lines that clear the settled amounts, per account with the party: Dr what was owed to the party (it is paid now), Cr what the party
    /// owed (it is deducted or repaid now). Accounts whose amounts cancel out get no line.
    /// </summary>
    public static IEnumerable<PostingLine> Lines(IEnumerable<Open> amounts, PostingParty party) =>
        amounts.GroupBy(a => a.AccountId).Select(g => (Account: g.Key, Amount: g.Sum(a => a.Amount))).Where(x => x.Amount != 0).OrderBy(x => x.Account)
            .Select(x => x.Amount > 0 ? new PostingLine(null, x.Account, x.Amount, 0, null, party) : new PostingLine(null, x.Account, 0, -x.Amount, null, party));
}
