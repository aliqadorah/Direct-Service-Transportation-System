using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// Partner payments (FR-085 … FR-087, 9.11): numbered; entry Dr partner payable (with the partner), Cr cash/bank; never above the
/// partner's current balance due (Σ partner cost of active trips − Σ active payments, checked under the payment-number lock so two
/// payments cannot together exceed it); void with reason (drv.settle): reversal, number kept. Owner findings 27 Oct 2026 (DT-277): the
/// partner's previous balance from manual / opening journal entries (DT-269) is settled with the next payment (set off when the partner owes
/// it, paid on top when owed to the partner) or by a repayment, cleared on the same accounts with the partner (<see cref="PreviousBalances"/>);
/// only the rest of a payment / repayment counts for the partner's trips (<see cref="TripPart"/>, DT-271 coverage).
/// </summary>
public sealed class PartnerPaymentService(IAppDbContext db, MasterDataSupport support, CashSupport cash, IChangeContext changes, IRecordLockGuard locks, IPeriodGuard periods,
    INumberingService numbering, PostingEngine posting, ISpreadsheetService sheets, IReportExporter exporter, AuditTrail audit, PreviousBalances previous)
{
    public const string EntityType = "TRN.PartnerPayment";
    public const string SourceType = "PartnerPayment";

    /// <summary>
    /// DT-277: what a payment paid for the partner's trips (its amount less the previous balance it settled), negative for a repayment (what came
    /// back for trips: its amount less the previous balance it settled). The same rule as trn.TRN_PartnerPaymentBoundary.
    /// </summary>
    public static readonly Expression<Func<PartnerPayment, decimal>> TripPart =
        p => p.Kind == CashCodes.Repayment ? -(p.Amount + p.PreviousBalance) : p.Amount - p.PreviousBalance;

    /// <summary>
    /// What the company owes a partner now for its trips: partner cost of active trips − what active payments paid for trips (+ repayments
    /// from the partner, DT-228; previous balances settled with them left out, DT-277). Below zero the partner owes the company (a trip voided
    /// or made cheaper after the partner was paid).
    /// </summary>
    public async Task<(decimal Cost, decimal Paid)> BalanceAsync(long partnerId, DateOnly? upTo, CancellationToken ct)
    {
        var tripsQ = db.Set<Trip>().Where(t => t.PartnerId == partnerId && t.Status == TripService.Active);
        var payQ = db.Set<PartnerPayment>().Where(p => p.PartnerId == partnerId && p.Status == CashCodes.Active);
        if (upTo is DateOnly d)
        {
            tripsQ = tripsQ.Where(t => t.TripDate <= d);
            payQ = payQ.Where(p => p.PaymentDate <= d);
        }
        var cost = await tripsQ.SumAsync(t => (decimal?)t.PartnerCost, ct).ConfigureAwait(false) ?? 0;
        var paid = await payQ.Select(TripPart).SumAsync(ct).ConfigureAwait(false);
        return (cost, paid);
    }

    /// <summary>
    /// DT-230: of what the partner owes now, the part chosen to be deducted from the next payment(s): amounts paid for trips voided or made
    /// cheaper with "Deduct", less what payments already set off; never more than the partner owes.
    /// </summary>
    public async Task<decimal> ToDeductAsync(long partnerId, CancellationToken ct)
    {
        var (cost, paid) = await BalanceAsync(partnerId, null, ct).ConfigureAwait(false);
        return Math.Min(await RemainingDeductAsync(partnerId, ct).ConfigureAwait(false), Math.Max(0, paid - cost));
    }

    private async Task<decimal> RemainingDeductAsync(long partnerId, CancellationToken ct)
    {
        var chosen = await db.Set<PartnerOverpayment>().Where(o => o.PartnerId == partnerId && o.Handling == CashCodes.Deduct).SumAsync(o => (decimal?)o.Amount, ct).ConfigureAwait(false) ?? 0;
        var setOff = await db.Set<PartnerPayment>().Where(p => p.PartnerId == partnerId && p.Status == CashCodes.Active).SumAsync(p => (decimal?)p.DeductedAmount, ct).ConfigureAwait(false) ?? 0;
        return Math.Max(0, chosen - setOff);
    }

    public async Task<(PartnerPaymentDetails Value, bool Created)> CreateAsync(PartnerPaymentRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<PartnerPayment>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        var notes = TextNormalizer.CleanText(r.Notes);
        new Validator().Check(!string.IsNullOrWhiteSpace(r.PartnerId), "partnerId", ErrorCodes.ValidationRequired).MaxLength("notes", notes, 500).ThrowIfInvalid();
        var (_, decimals) = await cash.CurrencyAsync(ct).ConfigureAwait(false);
        var kind = string.IsNullOrWhiteSpace(r.Kind) ? CashCodes.Payment
            : new[] { CashCodes.Payment, CashCodes.Repayment }.FirstOrDefault(k => string.Equals(k, r.Kind.Trim(), StringComparison.OrdinalIgnoreCase))
              ?? throw Fail.Field("kind", ErrorCodes.ValidationFormat, new FieldRef("kind"));
        // DT-277: a payment of 0 only sets off what the partner owes from manual / opening entries against its trips (checked below).
        var amount = kind == CashCodes.Payment && r.Amount == 0 && r.IncludePreviousBalance != false ? 0 : CashSupport.Amount(r.Amount, decimals);
        var date = await cash.DateAsync(r.PaymentDate, "paymentDate", ct).ConfigureAwait(false);
        // An archived partner can still be paid its balance due (DT-97).
        var partner = await cash.PartyAsync<Partner>("partnerId", r.PartnerId, ct).ConfigureAwait(false);
        var money = await cash.ChooseAsync(r.PaymentMethod, r.CashBoxId, r.BankAccountId, r.Reference, ct).ConfigureAwait(false);
        var payment = new PartnerPayment
        {
            Kind = kind,
            PartnerId = partner.Id, PaymentDate = date, Amount = amount, PaymentMethod = money.Method, CashBoxId = money.CashBoxId, BankAccountId = money.BankAccountId,
            Reference = money.Reference, Notes = notes, Status = CashCodes.Active,
        };
        if (clientId is Guid id) payment.PublicId = id;
        PartnerPayment? raced = null;
        try
        {
            await db.InTransactionAsync(async c =>
            {
                await posting.EnsurePostingReadyAsync(c).ConfigureAwait(false);
                await periods.EnsureOpenAsync(date, c).ConfigureAwait(false);
                payment.Number = await numbering.NextAsync(Configuration.DocumentTypes.PartnerPayment, c).ConfigureAwait(false);
                // Under the number lock: the balance cannot change through another payment meanwhile (FR-085).
                var (cost, paid) = await BalanceAsync(partner.Id, null, c).ConfigureAwait(false);
                var due = cost - paid;
                // DT-277: the previous balance up to the date, settled first — owed to the partner: paid from this payment (the rest pays trips);
                // owed by the partner: set off against the trips due (a payment, at most what the trips are owed) or repaid (a repayment).
                var open = r.IncludePreviousBalance == false ? [] : await previous.OpenAsync(AccountingCodes.PartyPartner, partner.Id, date, c).ConfigureAwait(false);
                var owedToPartner = open.Sum(o => o.Amount);
                var settle = kind == CashCodes.Payment
                    ? (owedToPartner > 0 ? Math.Min(amount, owedToPartner) : -Math.Min(-owedToPartner, Math.Max(due, 0)))
                    : (owedToPartner < 0 ? -Math.Min(amount, -owedToPartner) : 0);
                var items = PreviousBalances.Take(open, settle);
                payment.PreviousBalance = items.Sum(i => i.Amount);
                if (kind == CashCodes.Payment)
                {
                    if (amount == 0 && payment.PreviousBalance >= 0) throw Fail.Field("amount", ErrorCodes.ValidationRange, new FieldRef("amount"));
                    if (amount - payment.PreviousBalance > Math.Max(due, 0))
                        throw Fail.RuleOn("amount", ErrorCodes.PartnerPaymentExceedsBalance, Money.Format(amount), Money.Format(Math.Max(Math.Max(due, 0) + payment.PreviousBalance, 0)));
                    // DT-230: a balance due above zero means the newer trips have absorbed what was paid earlier for voided / cheaper trips: the
                    // part chosen to be deducted is set off with this payment (shown on its voucher and the statement; no money, no entry).
                    payment.DeductedAmount = await RemainingDeductAsync(partner.Id, c).ConfigureAwait(false);
                }
                // DT-228: a repayment never above what the partner owes the company (for trips, and from manual / opening entries).
                if (kind == CashCodes.Repayment && amount + payment.PreviousBalance > Math.Max(-due, 0))
                    throw Fail.RuleOn("amount", ErrorCodes.PartnerRepaymentExceedsOwed, Money.Format(amount), Money.Format(Math.Max(-due, 0) - Math.Min(owedToPartner, 0)));
                db.Set<PartnerPayment>().Add(payment);
                if (await support.SaveCreateAsync<PartnerPayment>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
                previous.Add(items, null, payment.Id);
                var entry = await posting.PostOrPendAsync(BuildEntry(payment, money.AccountId, items), c).ConfigureAwait(false);
                payment.JournalEntryId = entry?.Id;
                await db.SaveChangesAsync(c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (RacedCreate rc)
        {
            db.ChangeTracker.Clear();
            raced = rc.Existing;
        }
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(payment.Id, ct).ConfigureAwait(false), true);
    }

    private sealed class RacedCreate(PartnerPayment existing) : Exception
    {
        public PartnerPayment Existing { get; } = existing;
    }

    public async Task<PartnerPaymentDetails> VoidAsync(string token, CashVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var payment = await support.FindAsync<PartnerPayment>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, payment.Id, ct).ConfigureAwait(false);
        CashSupport.EnsureActive(payment, ErrorCodes.PartnerPaymentIsVoid);
        // DT-230 option (c): "the payment was not actually made", voided for a trip of this partner — recorded with that trip.
        Trip? forTrip = null;
        if (!string.IsNullOrWhiteSpace(r.ForTrip))
        {
            forTrip = await support.FindAsync<Trip>(r.ForTrip, ct).ConfigureAwait(false);
            if (forTrip.PartnerId != payment.PartnerId || payment.Kind != CashCodes.Payment) throw Fail.RuleOn("forTrip", ErrorCodes.PartnerPaymentTripMismatch);
        }
        CashSupport.RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(payment, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(payment.PaymentDate, c).ConfigureAwait(false);
            payment.Status = CashCodes.Void;
            payment.VoidReason = why;
            payment.VoidedBy = cash.UserId;
            payment.VoidedAt = DateTime.UtcNow;
            changes.SetAction(payment, "Void", why);
            if (forTrip is not null)
            {
                db.Set<PartnerOverpayment>().Add(new PartnerOverpayment
                {
                    PartnerId = payment.PartnerId, TripId = forTrip.Id, PartnerPaymentId = payment.Id, Amount = payment.Amount, Handling = CashCodes.NotPaid, Reason = why,
                });
                audit.Record(TripService.EntityType, forTrip.Id, "PartnerPaymentNotMade", $"{payment.Number} · {why}");
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await CashPosting.ReverseAsync(db, posting, payment, SourceType, payment.Kind == CashCodes.Repayment
                    ? (cash.En ? $"Void of partner repayment {payment.Number}" : $"إلغاء سداد الشريك {payment.Number}")
                    : (cash.En ? $"Void of partner payment {payment.Number}" : $"إلغاء دفعة الشريك {payment.Number}"), c)
                .ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(payment.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Partner payment entry (8.6): Dr partner payable with the partner, Cr cash / bank. A repayment from the partner (DT-228): Dr cash /
    /// bank, Cr partner payable with the partner. DT-277: partner payable takes only the trip part (amount ∓ previous balance); the previous
    /// balance is cleared on the accounts of its manual / opening lines with the partner (<see cref="PreviousBalances.Lines"/>).
    /// </summary>
    public PostingRequest BuildEntry(PartnerPayment p, long moneyAccountId, IReadOnlyList<PreviousBalances.Open> previousItems)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(previousItems);
        var party = new PostingParty(AccountingCodes.ModuleTrn, AccountingCodes.PartyPartner, p.PartnerId);
        var lines = new List<PostingLine>();
        if (p.Kind == CashCodes.Repayment)
        {
            if (p.Amount > 0) lines.Add(CashSupport.Debit(moneyAccountId, p.Amount));
            if (p.Amount + p.PreviousBalance > 0) lines.Add(PostingLine.CreditKey(PostingKeys.PartnerPayable, p.Amount + p.PreviousBalance, party));
            lines.AddRange(PreviousBalances.Lines(previousItems, party));
            return new PostingRequest(p.PaymentDate, (cash.En ? "Partner repayment " : "سداد من شريك ") + p.Number, AccountingCodes.ModuleTrn, SourceType, p.Id, false, lines);
        }
        if (p.Amount - p.PreviousBalance > 0) lines.Add(PostingLine.DebitKey(PostingKeys.PartnerPayable, p.Amount - p.PreviousBalance, party));
        lines.AddRange(PreviousBalances.Lines(previousItems, party));
        if (p.Amount > 0) lines.Add(CashSupport.Credit(moneyAccountId, p.Amount));
        return new PostingRequest(p.PaymentDate, (cash.En ? "Partner payment " : "دفعة شريك ") + p.Number, AccountingCodes.ModuleTrn, SourceType, p.Id, false, lines);
    }

    // ============================================================ reads

    internal sealed record Row(PartnerPayment P, Guid PartnerPid, string PartnerName, Guid? BoxPid, string? BoxName, Guid? BankPid, string? BankName, Guid? EntryPid,
        string? EntryNumber, bool Pending);

    internal IQueryable<Row> Rows(IQueryable<PartnerPayment> q) => q.Select(p => new Row(p, p.Partner.PublicId, p.Partner.Name,
        p.CashBox != null ? p.CashBox.PublicId : null, p.CashBox != null ? p.CashBox.Name : null,
        p.BankAccount != null ? p.BankAccount.PublicId : null, p.BankAccount != null ? p.BankAccount.Name : null,
        p.JournalEntry != null ? p.JournalEntry.PublicId : null, p.JournalEntry != null ? p.JournalEntry.Number : null,
        db.Set<PendingPosting>().Any(x => x.SourceModule == AccountingCodes.ModuleTrn && x.SourceType == SourceType && x.SourceId == p.Id && x.ResolvedAt == null)));

    private static readonly IReadOnlyDictionary<string, Expression<Func<PartnerPayment, object?>>> SortFields =
        new Dictionary<string, Expression<Func<PartnerPayment, object?>>>(StringComparer.Ordinal)
        {
            ["paymentDate"] = p => p.PaymentDate,
            ["number"] = p => p.Number,
            ["partner"] = p => p.Partner.Name,
            ["amount"] = p => p.Amount,
        };

    private IQueryable<PartnerPayment> Filter(PartnerPaymentListQuery q)
    {
        var query = CashSupport.WithStatus(db.Set<PartnerPayment>().AsNoTracking(), q.Status);
        if (!string.IsNullOrWhiteSpace(q.Partner))
        {
            var g = support.Decode<Partner>(q.Partner) ?? throw Fail.Field("partner", ErrorCodes.ValidationFormat, new FieldRef("partner"));
            query = query.Where(p => p.Partner.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(p => p.PaymentDate >= from);
        if (q.To is DateOnly to) query = query.Where(p => p.PaymentDate <= to);
        if (CashSupport.Method(q.Method) is { } m) query = query.Where(p => p.PaymentMethod == m);
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(p => p.Number.Contains(text) || (p.Reference != null && p.Reference.Contains(text)));
        return query;
    }

    public async Task<PartnerPaymentPage> ListAsync(PartnerPaymentListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = Filter(q);
        var paging = new PageRequest(q.Page, q.PageSize);
        var totals = await query.GroupBy(_ => 1).Select(g => new CashListTotals(g.Count(), g.Sum(p => p.Status == CashCodes.Active ? (p.Kind == CashCodes.Repayment ? -p.Amount : p.Amount) : 0), g.Count(p => p.Status == CashCodes.Active))).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? new CashListTotals(0, 0);
        var rows = await Rows(MasterDataSupport.Sort(query, q.Sort, SortFields, "-paymentDate").ThenByDescending(p => p.Id).Skip(paging.Skip).Take(paging.SafePageSize))
            .ToListAsync(ct).ConfigureAwait(false);
        return new PartnerPaymentPage(new PagedResult<PartnerPaymentResponse>(rows.Select(ToResponse).ToList(), paging.SafePage, paging.SafePageSize, totals.Count), totals);
    }

    public async Task<GeneratedFile> ExportAsync(PartnerPaymentListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var rows = await Rows(MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "-paymentDate").ThenByDescending(p => p.Id).Take(SheetData.MaxRows)).ToListAsync(ct).ConfigureAwait(false);
        string[] headers = cash.En
            ? ["Number", "Date", "Partner", "Amount", "Method", "Cash box / bank", "Reference", "Status", "Notes"]
            : ["الرقم", "التاريخ", "الشريك", "المبلغ", "طريقة الدفع", "الصندوق / البنك", "المرجع", "الحالة", "ملاحظات"];
        var data = rows.Select(x => (IReadOnlyList<object?>)[x.P.Number, x.P.PaymentDate, x.PartnerName, x.P.Amount, x.P.PaymentMethod, x.BoxName ?? x.BankName, x.P.Reference,
            x.P.Status, x.P.Notes]).ToList();
        return await CashExport.FileAsync("partner-payments", format, headers, data, sheets, exporter, ct).ConfigureAwait(false);
    }

    public async Task<PartnerPaymentDetails> GetAsync(string token, CancellationToken ct)
    {
        var p = await support.FindAsync<PartnerPayment>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(p.Id, ct).ConfigureAwait(false);
    }

    internal async Task<PartnerPaymentDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<PartnerPayment>().AsNoTracking().Where(p => p.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        var settled = await previous.OfDocumentAsync(null, id, ct).ConfigureAwait(false);
        return new PartnerPaymentDetails(ToResponse(row), await cash.WordsAsync(row.P.Amount, ct).ConfigureAwait(false), await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false),
            await previous.EntriesAsync(settled, ct).ConfigureAwait(false));
    }

    internal PartnerPaymentResponse ToResponse(Row x) => new(support.Token<PartnerPayment>(x.P.PublicId), x.P.Number, new Ref(support.Token<Partner>(x.PartnerPid), x.PartnerName),
        x.P.PaymentDate, x.P.Amount, x.P.PaymentMethod, cash.Account(x.BoxPid, x.BoxName, x.BankPid, x.BankName), x.P.Reference, x.P.Notes, x.P.Status,
        cash.Entry(x.EntryPid, x.EntryNumber), x.Pending, x.P.VoidReason, DateTime.SpecifyKind(x.P.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.P.RowVersion),
        x.P.Kind, x.P.DeductedAmount, x.P.PreviousBalance);
}

/// <summary>Pending-posting retries for partner payments.</summary>
public sealed class PartnerPaymentPostingHandler(IAppDbContext db, IUrlTokenService tokens, PartnerPaymentService payments, CashSupport cash, PreviousBalances previous)
    : CashPostingHandler<PartnerPayment>(db, tokens)
{
    public override string SourceType => PartnerPaymentService.SourceType;

    protected override string VoidCode => ErrorCodes.PartnerPaymentIsVoid;

    protected override async Task<PostingRequest?> EntryAsync(PartnerPayment d, CancellationToken ct) =>
        payments.BuildEntry(d, await cash.AccountIdAsync(d.CashBoxId, d.BankAccountId, ct).ConfigureAwait(false), await previous.OfDocumentAsync(null, d.Id, ct).ConfigureAwait(false));
}
