using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// Partner payments (FR-085 … FR-087, 9.11): numbered; entry Dr partner payable (with the partner), Cr cash/bank; never above the
/// partner's current balance due (Σ partner cost of active trips − Σ active payments, checked under the payment-number lock so two
/// payments cannot together exceed it); void with reason (drv.settle): reversal, number kept.
/// </summary>
public sealed class PartnerPaymentService(IAppDbContext db, MasterDataSupport support, CashSupport cash, IChangeContext changes, IRecordLockGuard locks, IPeriodGuard periods,
    INumberingService numbering, PostingEngine posting, ISpreadsheetService sheets)
{
    public const string EntityType = "TRN.PartnerPayment";
    public const string SourceType = "PartnerPayment";

    /// <summary>What the company owes a partner now: partner cost of active trips − active payments.</summary>
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
        var paid = await payQ.SumAsync(p => (decimal?)p.Amount, ct).ConfigureAwait(false) ?? 0;
        return (cost, paid);
    }

    public async Task<(PartnerPaymentDetails Value, bool Created)> CreateAsync(PartnerPaymentRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<PartnerPayment>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        var notes = TextNormalizer.CleanText(r.Notes);
        new Validator().Check(!string.IsNullOrWhiteSpace(r.PartnerId), "partnerId", ErrorCodes.ValidationRequired).MaxLength("notes", notes, 500).ThrowIfInvalid();
        var (_, decimals) = await cash.CurrencyAsync(ct).ConfigureAwait(false);
        var amount = CashSupport.Amount(r.Amount, decimals);
        var date = await cash.DateAsync(r.PaymentDate, "paymentDate", ct).ConfigureAwait(false);
        // An archived partner can still be paid its balance due (DT-97).
        var partner = await cash.PartyAsync<Partner>("partnerId", r.PartnerId, ct).ConfigureAwait(false);
        var money = await cash.ChooseAsync(r.PaymentMethod, r.CashBoxId, r.BankAccountId, r.Reference, ct).ConfigureAwait(false);
        var payment = new PartnerPayment
        {
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
                if (amount > cost - paid) throw Fail.RuleOn("amount", ErrorCodes.PartnerPaymentExceedsBalance, Money.Format(amount), Money.Format(cost - paid));
                db.Set<PartnerPayment>().Add(payment);
                if (await support.SaveCreateAsync<PartnerPayment>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
                var entry = await posting.PostOrPendAsync(BuildEntry(payment, money.AccountId), c).ConfigureAwait(false);
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
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await CashPosting.ReverseAsync(db, posting, payment, SourceType, cash.En ? $"Void of partner payment {payment.Number}" : $"إلغاء دفعة الشريك {payment.Number}", c)
                .ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(payment.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Partner payment entry (8.6): Dr partner payable with the partner, Cr cash / bank.</summary>
    public PostingRequest BuildEntry(PartnerPayment p, long moneyAccountId)
    {
        ArgumentNullException.ThrowIfNull(p);
        var party = new PostingParty(AccountingCodes.ModuleTrn, AccountingCodes.PartyPartner, p.PartnerId);
        return new PostingRequest(p.PaymentDate, (cash.En ? "Partner payment " : "دفعة شريك ") + p.Number, AccountingCodes.ModuleTrn, SourceType, p.Id, false,
            [PostingLine.DebitKey(PostingKeys.PartnerPayable, p.Amount, party), CashSupport.Credit(moneyAccountId, p.Amount)]);
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
            var g = support.Decode<Partner>(q.Partner) ?? throw Fail.Field("partner", ErrorCodes.ValidationFormat, "partner");
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
        var totals = await query.GroupBy(_ => 1).Select(g => new CashListTotals(g.Count(), g.Sum(p => p.Status == CashCodes.Active ? p.Amount : 0))).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? new CashListTotals(0, 0);
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
        return CashExport.File("partner-payments", format, headers, data, sheets);
    }

    public async Task<PartnerPaymentDetails> GetAsync(string token, CancellationToken ct)
    {
        var p = await support.FindAsync<PartnerPayment>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(p.Id, ct).ConfigureAwait(false);
    }

    internal async Task<PartnerPaymentDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<PartnerPayment>().AsNoTracking().Where(p => p.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        return new PartnerPaymentDetails(ToResponse(row), await cash.WordsAsync(row.P.Amount, ct).ConfigureAwait(false), await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }

    internal PartnerPaymentResponse ToResponse(Row x) => new(support.Token<PartnerPayment>(x.P.PublicId), x.P.Number, new Ref(support.Token<Partner>(x.PartnerPid), x.PartnerName),
        x.P.PaymentDate, x.P.Amount, x.P.PaymentMethod, cash.Account(x.BoxPid, x.BoxName, x.BankPid, x.BankName), x.P.Reference, x.P.Notes, x.P.Status,
        cash.Entry(x.EntryPid, x.EntryNumber), x.Pending, x.P.VoidReason, DateTime.SpecifyKind(x.P.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.P.RowVersion));
}

/// <summary>Pending-posting retries for partner payments.</summary>
public sealed class PartnerPaymentPostingHandler(IAppDbContext db, IUrlTokenService tokens, PartnerPaymentService payments, CashSupport cash)
    : CashPostingHandler<PartnerPayment>(db, tokens)
{
    public override string SourceType => PartnerPaymentService.SourceType;

    protected override string VoidCode => ErrorCodes.PartnerPaymentIsVoid;

    protected override async Task<PostingRequest?> EntryAsync(PartnerPayment d, CancellationToken ct) =>
        payments.BuildEntry(d, await cash.AccountIdAsync(d.CashBoxId, d.BankAccountId, ct).ConfigureAwait(false));
}
