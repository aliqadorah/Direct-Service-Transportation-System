using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Invoicing;

/// <summary>
/// Credit notes (FR-049 … FR-053, BR-025 … BR-027): created from an approved invoice by selecting returned trips (whole trips) or
/// manual lines and quantities, with a reason; the returned share carries its part of discount, fees and tax; never above the
/// invoice's remaining balance; a credit note returning everything still open leaves exactly 0.000; own number sequence; posts the
/// mirror of the invoice entry for the returned share; returned trips become unbilled. Void restores the invoice and its trips
/// unless a trip was invoiced again or the period is closed.
/// </summary>
public sealed class CreditNoteService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks,
    IPeriodGuard periods, INumberingService numbering, PostingEngine posting, InvoiceService invoices)
{
    public const string EntityType = "TRN.CreditNote";
    public const string SourceType = "CreditNote";

    private bool En => ctx.Language == "en";

    public async Task<(CreditNoteDetails Value, bool Created)> CreateAsync(CreditNoteRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<CreditNote>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        var reason = TextNormalizer.CleanText(r.Reason);
        new Validator().Check(!string.IsNullOrWhiteSpace(r.InvoiceId), "invoiceId", ErrorCodes.ValidationRequired).Check(r.CreditDate is not null, "creditDate", ErrorCodes.ValidationRequired)
            .Require("reason", reason).MaxLength("reason", reason, 500).Check((r.Lines ?? []).Count > 0, "lines", ErrorCodes.ValidationRequired).ThrowIfInvalid();
        var inv = await support.FindAsync<Invoice>(r.InvoiceId, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(InvoiceService.EntityType, inv.Id, ct).ConfigureAwait(false);
        var company = await invoices.CompanyAsync(ct).ConfigureAwait(false);
        if (inv.Status is not (InvoiceCodes.Approved or InvoiceCodes.Closed)) throw Fail.Rule(ErrorCodes.CreditInvoiceNotApproved, inv.Number);
        if (r.CreditDate > company.Today) throw Fail.Field("creditDate", ErrorCodes.DateFuture, "creditDate");
        if (r.CreditDate < inv.InvoiceDate) throw Fail.Field("creditDate", ErrorCodes.ValidationRange, "creditDate");

        var cn = new CreditNote { InvoiceId = inv.Id, CustomerId = inv.CustomerId, CreditDate = r.CreditDate!.Value, Reason = reason!, Status = InvoiceCodes.Posted };
        if (clientId is Guid id) cn.PublicId = id;
        CreditNote? raced = null;
        try
        {
            await db.InTransactionAsync(async c =>
        {
            await posting.EnsurePostingReadyAsync(c).ConfigureAwait(false);
            await periods.EnsureOpenAsync(cn.CreditDate, c).ConfigureAwait(false);
            var invLines = await db.Set<InvoiceLine>().Where(l => l.InvoiceId == inv.Id).ToListAsync(c).ConfigureAwait(false);
            var returnedQty = await db.Set<CreditNoteLine>().Where(x => x.InvoiceLine.InvoiceId == inv.Id && x.CreditNote.Status == InvoiceCodes.Posted)
                .GroupBy(x => x.InvoiceLineId).Select(g => new { g.Key, Qty = g.Sum(x => x.Quantity), Amount = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.Key, x => (x.Qty, x.Amount), c).ConfigureAwait(false);

            // ---- returned lines: trip lines whole, manual lines up to the quantity not yet returned (FR-049).
            var picked = new List<(InvoiceLine Line, decimal Qty, decimal Amount)>();
            var seen = new HashSet<long>();
            var items = r.Lines!;
            for (var i = 0; i < items.Count; i++)
            {
                var field = $"lines[{i}].invoiceLineId";
                var pid = support.Decode<InvoiceLine>(items[i].InvoiceLineId) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, field);
                var line = invLines.FirstOrDefault(l => l.PublicId == pid) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, field);
                if (!seen.Add(line.Id)) throw Fail.Field(field, ErrorCodes.ImportDuplicate, field);
                var (doneQty, doneAmount) = returnedQty.GetValueOrDefault(line.Id);
                var openQty = line.Quantity - doneQty;
                if (openQty <= 0) throw Fail.RuleOn(field, ErrorCodes.CreditLineReturned);
                decimal qty;
                if (line.TripId is not null) qty = openQty;
                else
                {
                    qty = items[i].Quantity ?? openQty;
                    if (qty <= 0 || decimal.Round(qty, 3) != qty || qty > openQty) throw Fail.Field($"lines[{i}].quantity", ErrorCodes.ValidationRange, "quantity");
                }
                // The last open part of a line takes exactly what is left of its amount (no rounding residue on the line).
                var amount = qty == openQty ? line.Amount - doneAmount : InvoiceCalculator.LineAmount(qty, line.UnitPrice, company.Decimals);
                picked.Add((line, qty, amount));
            }

            var everything = invLines.All(l => l.Quantity - returnedQty.GetValueOrDefault(l.Id).Qty - picked.Where(p => p.Line.Id == l.Id).Sum(p => p.Qty) <= 0);
            var before = await db.Set<CreditNote>().Where(x => x.InvoiceId == inv.Id && x.Status == InvoiceCodes.Posted)
                .GroupBy(_ => 1).Select(g => new ReturnedSoFar(g.Sum(x => x.Subtotal), g.Sum(x => x.DiscountShare), g.Sum(x => x.FeesShare), g.Sum(x => x.Tax), g.Sum(x => x.Total)))
                .SingleOrDefaultAsync(c).ConfigureAwait(false) ?? ReturnedSoFar.None;
            var invoiceTotals = new InvoiceTotals(inv.Subtotal, inv.Discount, inv.ExtraFees, inv.BaseAmount, inv.Tax, inv.Total);
            // The invoice's own tax mode (review M1): an inclusive invoice stores a net subtotal below the sum of its line amounts.
            var linesSum = invLines.Sum(l => l.Amount);
            var inclusive = inv.Subtotal != linesSum;
            var t = InvoiceCalculator.Credit(picked.Sum(p => p.Amount), linesSum, invoiceTotals, inv.TaxRate, inclusive, everything, before, company.Decimals);
            t = InvoiceCalculator.CapToRemaining(t, inv.RemainingAmount, 0.010m);
            // BR-026: never above the remaining balance.
            if (t.Total > inv.RemainingAmount) throw Fail.Rule(ErrorCodes.CreditExceedsRemaining, Money.Format(t.Total), Money.Format(inv.RemainingAmount));
            if (t.Total <= 0) throw Fail.Rule(ErrorCodes.InvoiceTotalZero);

            cn.Number = await numbering.NextAsync(Configuration.DocumentTypes.CreditNote, c).ConfigureAwait(false);
            cn.Subtotal = t.Subtotal;
            cn.DiscountShare = t.DiscountShare;
            cn.FeesShare = t.FeesShare;
            cn.Tax = t.Tax;
            cn.Total = t.Total;
            db.Set<CreditNote>().Add(cn);
            // A repeated create racing this one: roll back (the number taken here must not leave a gap, BR-038) and return the other.
            if (await support.SaveCreateAsync<CreditNote>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
            foreach (var p in picked)
                db.Set<CreditNoteLine>().Add(new CreditNoteLine { CreditNoteId = cn.Id, InvoiceLineId = p.Line.Id, TripId = p.Line.TripId, Quantity = p.Qty, Amount = p.Amount });
            // Returned trips become available for invoicing again (FR-051); each records the credit note in its history (FR-015).
            var tripIds = picked.Where(p => p.Line.TripId is not null).Select(p => p.Line.TripId!.Value).ToList();
            foreach (var trip in await db.Set<Trip>().Where(x => tripIds.Contains(x.Id)).ToListAsync(c).ConfigureAwait(false))
            {
                trip.InvoiceId = null;
                changes.SetAction(trip, "ReturnedByCreditNote", cn.Number);
            }
            inv.ReturnedAmount += cn.Total;
            InvoiceService.Rebalance(inv);
            changes.SetAction(inv, "CreditNote", cn.Number);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            var entry = await posting.PostOrPendAsync(BuildEntry(cn, inv)!, c).ConfigureAwait(false);
            cn.JournalEntryId = entry?.Id;
            await db.SaveChangesAsync(c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        }
        catch (RacedCreate r2)
        {
            db.ChangeTracker.Clear();
            raced = r2.Existing;
        }
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(cn.Id, ct).ConfigureAwait(false), true);
    }

    /// <summary>Void (FR-052): reversal, trips invoiced again; blocked if a returned trip was invoiced again or the period is closed.</summary>
    public async Task<CreditNoteDetails> VoidAsync(string token, CreditNoteVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var cn = await support.FindAsync<CreditNote>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, cn.Id, ct).ConfigureAwait(false);
        if (cn.Status == InvoiceCodes.Void) throw Fail.Rule(ErrorCodes.CreditIsVoid, cn.Number);
        if (string.IsNullOrEmpty(r.RowVersion)) throw Fail.Field("rowVersion", ErrorCodes.ValidationRequired, "rowVersion");
        db.ExpectRowVersion(cn, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(cn.CreditDate, c).ConfigureAwait(false);
            var tripIds = await db.Set<CreditNoteLine>().Where(l => l.CreditNoteId == cn.Id && l.TripId != null).Select(l => l.TripId!.Value).ToListAsync(c).ConfigureAwait(false);
            var tripsBack = await db.Set<Trip>().Where(t => tripIds.Contains(t.Id)).ToListAsync(c).ConfigureAwait(false);
            if (tripsBack.FirstOrDefault(t => t.InvoiceId is not null || t.Status != Trips.TripService.Active) is { } taken)
                throw Fail.Rule(ErrorCodes.CreditTripReinvoiced, taken.ReceiptNo);
            // A returned trip edited since (its revenue no longer the returned amount) cannot go back onto the invoice (review M5).
            var returnedAmounts = await db.Set<CreditNoteLine>().Where(l => l.CreditNoteId == cn.Id && l.TripId != null).ToDictionaryAsync(l => l.TripId!.Value, l => l.Amount, c).ConfigureAwait(false);
            if (tripsBack.FirstOrDefault(t => returnedAmounts.TryGetValue(t.Id, out var a) && a != t.Revenue) is { } changed)
                throw Fail.Rule(ErrorCodes.CreditTripChanged, changed.ReceiptNo);
            var inv = await db.Set<Invoice>().SingleAsync(i => i.Id == cn.InvoiceId, c).ConfigureAwait(false);
            if (inv.Status == InvoiceCodes.Void) throw Fail.Rule(ErrorCodes.InvoiceIsVoid, inv.Number);
            cn.Status = InvoiceCodes.Void;
            cn.VoidReason = why;
            cn.VoidedBy = ctx.UserId ?? throw new InvalidOperationException("A void needs a user.");
            cn.VoidedAt = DateTime.UtcNow;
            changes.SetAction(cn, "Void", why);
            foreach (var t in tripsBack)
            {
                t.InvoiceId = inv.Id;
                changes.SetAction(t, "Invoiced", inv.Number);
            }
            inv.ReturnedAmount -= cn.Total;
            InvoiceService.Rebalance(inv);
            changes.SetAction(inv, "CreditNoteVoided", cn.Number);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            if (cn.JournalEntryId is long entryId)
            {
                var entry = await db.Set<JournalEntry>().SingleAsync(e => e.Id == entryId, c).ConfigureAwait(false);
                if (entry.Status == AccountingCodes.Posted)
                    await posting.ReverseAsync(entry, $"{(En ? "Void of credit note" : "إلغاء الإشعار الدائن")} {cn.Number}", null, c).ConfigureAwait(false);
            }
            await invoices.ResolvePendingAsync(SourceType, cn.Id, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(cn.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Mirror of the invoice entry for the returned share (8.6): Dr transport revenue, other revenue, sales tax output; Cr discount allowed, trade receivables.</summary>
    public PostingRequest? BuildEntry(CreditNote cn, Invoice inv)
    {
        ArgumentNullException.ThrowIfNull(cn);
        ArgumentNullException.ThrowIfNull(inv);
        if (cn.Status != InvoiceCodes.Posted) return null;
        var party = new PostingParty(AccountingCodes.ModuleTrn, AccountingCodes.PartyCustomer, cn.CustomerId);
        var lines = new List<PostingLine>();
        if (cn.Subtotal > 0) lines.Add(PostingLine.DebitKey(PostingKeys.TransportRevenue, cn.Subtotal));
        if (cn.FeesShare > 0) lines.Add(PostingLine.DebitKey(PostingKeys.OtherRevenue, cn.FeesShare));
        if (cn.Tax > 0) lines.Add(PostingLine.DebitKey(PostingKeys.SalesTaxOutput, cn.Tax));
        if (cn.DiscountShare > 0) lines.Add(PostingLine.CreditKey(PostingKeys.DiscountAllowed, cn.DiscountShare));
        lines.Add(PostingLine.CreditKey(PostingKeys.TradeReceivables, cn.Total, party));
        return new PostingRequest(cn.CreditDate, $"{(En ? "Credit note" : "إشعار دائن")} {cn.Number} · {inv.Number}", AccountingCodes.ModuleTrn, SourceType, cn.Id, false, lines);
    }

    private sealed class RacedCreate(CreditNote existing) : Exception
    {
        public CreditNote Existing { get; } = existing;
    }

    // ============================================================ reads

    internal sealed record Row(CreditNote C, Guid InvoicePid, string? InvoiceNumber, Guid CustomerPid, string CustomerName, Guid? EntryPid, string? EntryNumber, bool Pending, string? EStatus);

    private IQueryable<Row> Rows(IQueryable<CreditNote> q) => q.Select(c => new Row(c, c.Invoice.PublicId, c.Invoice.Number, c.Customer.PublicId, c.Customer.Name,
        c.JournalEntry != null ? c.JournalEntry.PublicId : null, c.JournalEntry != null ? c.JournalEntry.Number : null,
        db.Set<PendingPosting>().Any(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == SourceType && p.SourceId == c.Id && p.ResolvedAt == null),
        db.Set<EInvoiceSubmission>().Where(s => s.CreditNoteId == c.Id).OrderByDescending(s => s.Id).Select(s => s.Status).FirstOrDefault()));

    private static readonly IReadOnlyDictionary<string, Expression<Func<CreditNote, object?>>> SortFields = new Dictionary<string, Expression<Func<CreditNote, object?>>>(StringComparer.Ordinal)
    {
        ["creditDate"] = c => c.CreditDate,
        ["number"] = c => c.Number,
        ["customer"] = c => c.Customer.Name,
        ["total"] = c => c.Total,
    };

    public async Task<PagedResult<CreditNoteResponse>> ListAsync(CreditNoteListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = db.Set<CreditNote>().AsNoTracking();
        query = (q.Status?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "posted" => query.Where(c => c.Status == InvoiceCodes.Posted),
            "void" => query.Where(c => c.Status == InvoiceCodes.Void),
            _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, "status"),
        };
        if (!string.IsNullOrWhiteSpace(q.Customer))
        {
            var g = support.Decode<Customer>(q.Customer) ?? throw Fail.Field("customer", ErrorCodes.ValidationFormat, "customer");
            query = query.Where(c => c.Customer.PublicId == g);
        }
        if (!string.IsNullOrWhiteSpace(q.Invoice))
        {
            var g = support.Decode<Invoice>(q.Invoice) ?? throw Fail.Field("invoice", ErrorCodes.ValidationFormat, "invoice");
            query = query.Where(c => c.Invoice.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(c => c.CreditDate >= from);
        if (q.To is DateOnly to) query = query.Where(c => c.CreditDate <= to);
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(c => c.Number.Contains(text) || (c.Invoice.Number != null && c.Invoice.Number.Contains(text)));
        var paging = new PageRequest(q.Page, q.PageSize);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await Rows(MasterDataSupport.Sort(query, q.Sort, SortFields, "-creditDate").ThenByDescending(c => c.Id).Skip(paging.Skip).Take(paging.SafePageSize))
            .ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<CreditNoteResponse>(rows.Select(ToResponse).ToList(), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<CreditNoteDetails> GetAsync(string token, CancellationToken ct)
    {
        var cn = await support.FindAsync<CreditNote>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(cn.Id, ct).ConfigureAwait(false);
    }

    internal async Task<CreditNoteDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<CreditNote>().AsNoTracking().Where(c => c.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        var lines = (await db.Set<CreditNoteLine>().AsNoTracking().Where(l => l.CreditNoteId == id).OrderBy(l => l.Id)
                .Select(l => new { l.PublicId, LinePid = l.InvoiceLine.PublicId, l.InvoiceLine.Description, TripPid = l.Trip != null ? (Guid?)l.Trip.PublicId : null,
                    Receipt = l.Trip != null ? l.Trip.ReceiptNo : null, l.Quantity, l.Amount }).ToListAsync(ct).ConfigureAwait(false))
            .Select(l => new CreditNoteLineResponse(support.Token<CreditNoteLine>(l.PublicId), support.Token<InvoiceLine>(l.LinePid),
                l.TripPid is Guid t ? new Ref(support.Token<Trip>(t), l.Receipt!) : null, l.Description, l.Quantity, l.Amount)).ToList();
        var company = await invoices.CompanyAsync(ct).ConfigureAwait(false);
        var words = En ? AmountInWords.English(row.C.Total, company.CurrencyCode, company.Decimals) : AmountInWords.Arabic(row.C.Total, company.CurrencyCode, company.Decimals);
        return new CreditNoteDetails(ToResponse(row), lines, words, await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }

    private CreditNoteResponse ToResponse(Row r)
    {
        var c = r.C;
        return new CreditNoteResponse(support.Token<CreditNote>(c.PublicId), c.Number, new DocRef(support.Token<Invoice>(r.InvoicePid), r.InvoiceNumber),
            new Ref(support.Token<Customer>(r.CustomerPid), r.CustomerName), c.CreditDate, c.Reason, c.Subtotal, c.DiscountShare, c.FeesShare,
            c.Subtotal - c.DiscountShare + c.FeesShare, c.Tax, c.Total, c.Status, r.EntryPid is Guid e ? new DocRef(support.Token<JournalEntry>(e), r.EntryNumber) : null,
            r.Pending, c.VoidReason, r.EStatus ?? "NotRequired", DateTime.SpecifyKind(c.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(c.RowVersion));
    }
}
