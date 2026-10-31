using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Accounting.Contracts.Posting;
using DirectService.Accounting.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// Driver advances (FR-072 … FR-075, 9.10): numbered from the advance sequence; entry Dr driver advances (with the driver), Cr cash/bank;
/// editable only while unsettled and outside closed periods — the entry is reversed and posted again (BR-035) and the change recorded
/// old → new; void with reason (reversal, number kept), refused once settled (BR-032).
/// </summary>
public sealed class AdvanceService(IAppDbContext db, MasterDataSupport support, CashSupport cash, IChangeContext changes, IRecordLockGuard locks, IPeriodGuard periods,
    INumberingService numbering, IAccountingPosting posting, ISpreadsheetService sheets, IReportExporter exporter)
{
    public const string EntityType = "TRN.Advance";
    public const string SourceType = "Advance";

    private sealed record Input(Driver Driver, DateOnly Date, decimal Amount, MoneyChoice Money, string? Notes);

    private async Task<Input> ReadAsync(AdvanceRequest r, Advance? current, CancellationToken ct)
    {
        var notes = TextNormalizer.CleanText(r.Notes);
        new Validator().Check(!string.IsNullOrWhiteSpace(r.DriverId), "driverId", ErrorCodes.ValidationRequired).MaxLength("notes", notes, 500).ThrowIfInvalid();
        var (_, decimals) = await cash.CurrencyAsync(ct).ConfigureAwait(false);
        var amount = CashSupport.Amount(r.Amount, decimals);
        var date = await cash.DateAsync(r.AdvanceDate, "advanceDate", ct).ConfigureAwait(false);
        var driver = await support.ChooseAsync<Driver>("driverId", r.DriverId, current?.DriverId, d => d.FullName, ct).ConfigureAwait(false);
        var money = await cash.ChooseAsync(r.PaymentMethod, r.CashBoxId, r.BankAccountId, r.Reference, ct, current?.CashBoxId, current?.BankAccountId).ConfigureAwait(false);
        return new Input(driver, date, amount, money, notes);
    }

    public async Task<(AdvanceDetails Value, bool Created)> CreateAsync(AdvanceRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Advance>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        var x = await ReadAsync(r, null, ct).ConfigureAwait(false);
        var advance = new Advance
        {
            DriverId = x.Driver.Id, AdvanceDate = x.Date, Amount = x.Amount, PaymentMethod = x.Money.Method, CashBoxId = x.Money.CashBoxId, BankAccountId = x.Money.BankAccountId,
            Reference = x.Money.Reference, Notes = x.Notes, Status = CashCodes.Active,
        };
        if (clientId is Guid id) advance.PublicId = id;
        Advance? raced = null;
        try
        {
            await db.InTransactionAsync(async c =>
            {
                await posting.EnsureReadyAsync(AccountingCodes.ModuleTrn, c).ConfigureAwait(false);
                await periods.EnsureOpenAsync(x.Date, c).ConfigureAwait(false);
                advance.Number = await numbering.NextAsync(Configuration.DocumentTypes.Advance, c).ConfigureAwait(false);
                db.Set<Advance>().Add(advance);
                if (await support.SaveCreateAsync<Advance>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
                await PostAsync(advance, x.Money.AccountId, c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (RacedCreate rc)
        {
            db.ChangeTracker.Clear();
            raced = rc.Existing;
        }
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(advance.Id, ct).ConfigureAwait(false), true);
    }

    private sealed class RacedCreate(Advance existing) : Exception
    {
        public Advance Existing { get; } = existing;
    }

    /// <summary>Edit (FR-073): only while unsettled and not in a closed period (old and new date); reverse + re-post when the entry changes.</summary>
    public async Task<AdvanceDetails> UpdateAsync(string token, AdvanceRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var advance = await support.FindAsync<Advance>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, advance.Id, ct).ConfigureAwait(false);
        CashSupport.EnsureActive(advance, ErrorCodes.AdvanceIsVoid);
        await EnsureUnsettledAsync(advance, ct).ConfigureAwait(false);
        await EnsureNotCarriedAsync(advance, ct).ConfigureAwait(false);
        CashSupport.RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(advance, r.RowVersion);
        var x = await ReadAsync(r, advance, ct).ConfigureAwait(false);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(advance.AdvanceDate, c).ConfigureAwait(false);
            await periods.EnsureOpenAsync(x.Date, c).ConfigureAwait(false);
            var repost = advance.DriverId != x.Driver.Id || advance.AdvanceDate != x.Date || advance.Amount != x.Amount
                || advance.CashBoxId != x.Money.CashBoxId || advance.BankAccountId != x.Money.BankAccountId || advance.JournalEntryId is null;
            if (repost)
            {
                await CashPosting.ReverseAsync(db, posting, advance, SourceType, cash.En ? $"Edit of advance {advance.Number}" : $"تعديل السلفة {advance.Number}", c).ConfigureAwait(false);
                advance.JournalEntryId = null;
            }
            advance.DriverId = x.Driver.Id;
            advance.AdvanceDate = x.Date;
            advance.Amount = x.Amount;
            advance.PaymentMethod = x.Money.Method;
            advance.CashBoxId = x.Money.CashBoxId;
            advance.BankAccountId = x.Money.BankAccountId;
            advance.Reference = x.Money.Reference;
            advance.Notes = x.Notes;
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            if (repost) await PostAsync(advance, x.Money.AccountId, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(advance.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Void (FR-074, drv.advance): reason, reversal, number kept; refused once settled (BR-032) or in a closed period.</summary>
    public async Task<AdvanceDetails> VoidAsync(string token, CashVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var advance = await support.FindAsync<Advance>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, advance.Id, ct).ConfigureAwait(false);
        CashSupport.EnsureActive(advance, ErrorCodes.AdvanceIsVoid);
        await EnsureUnsettledAsync(advance, ct).ConfigureAwait(false);
        await EnsureNotCarriedAsync(advance, ct).ConfigureAwait(false);
        CashSupport.RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(advance, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(advance.AdvanceDate, c).ConfigureAwait(false);
            advance.Status = CashCodes.Void;
            advance.VoidReason = why;
            advance.VoidedBy = cash.UserId;
            advance.VoidedAt = DateTime.UtcNow;
            changes.SetAction(advance, "Void", why);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await CashPosting.ReverseAsync(db, posting, advance, SourceType, cash.En ? $"Void of advance {advance.Number}" : $"إلغاء السلفة {advance.Number}", c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(advance.Id, ct).ConfigureAwait(false);
    }

    private async Task EnsureUnsettledAsync(Advance a, CancellationToken ct)
    {
        if (a.SettlementId is not long s) return;
        var number = await db.Set<Settlement>().Where(x => x.Id == s).Select(x => x.Number).SingleAsync(ct).ConfigureAwait(false);
        throw Fail.Rule(ErrorCodes.AdvanceSettled, a.Number, number);
    }

    /// <summary>DT-228: an advance carrying a settlement's unpaid rest changes only with that settlement (void the settlement).</summary>
    private async Task EnsureNotCarriedAsync(Advance a, CancellationToken ct)
    {
        if (a.CarriedFromSettlementId is not long s) return;
        var number = await db.Set<Settlement>().Where(x => x.Id == s).Select(x => x.Number).SingleAsync(ct).ConfigureAwait(false);
        throw Fail.Rule(ErrorCodes.AdvanceCarried, a.Number, number);
    }

    private async Task PostAsync(Advance a, long moneyAccountId, CancellationToken ct)
    {
        var entry = await posting.PostOrPendAsync(BuildEntry(a, moneyAccountId), ct).ConfigureAwait(false);
        a.JournalEntryId = entry?.JournalEntryId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Advance entry (8.6): Dr driver advances with the driver, Cr cash / bank.</summary>
    public PostingDocument BuildEntry(Advance a, long moneyAccountId)
    {
        ArgumentNullException.ThrowIfNull(a);
        var party = new PartyRef(AccountingCodes.ModuleTrn, AccountingCodes.PartyDriver, a.DriverId);
        return PostingEntry.Of(a.AdvanceDate, (cash.En ? "Driver advance " : "سلفة سائق ") + a.Number, AccountingCodes.ModuleTrn, SourceType, a.Id,
            [PostingLineSpec.DebitKey(PostingKeys.DriverAdvances, a.Amount, party), CashSupport.Credit(moneyAccountId, a.Amount)]);
    }

    // ============================================================ reads

    internal sealed record Row(Advance A, Guid DriverPid, string DriverName, Guid? SettlementPid, string? SettlementNumber, Guid? BoxPid, string? BoxName, Guid? BankPid,
        string? BankName, Guid? EntryPid, string? EntryNumber, bool Pending, Guid? CarriedPid, string? CarriedNumber);

    private IQueryable<Row> Rows(IQueryable<Advance> q) => q.Select(a => new Row(a, a.Driver.PublicId, a.Driver.FullName,
        a.Settlement != null ? a.Settlement.PublicId : null, a.Settlement != null ? a.Settlement.Number : null,
        db.Set<CashBox>().Where(z => z.Id == a.CashBoxId).Select(z => (Guid?)z.PublicId).FirstOrDefault(), db.Set<CashBox>().Where(z => z.Id == a.CashBoxId).Select(z => z.Name).FirstOrDefault(),
        db.Set<BankAccount>().Where(z => z.Id == a.BankAccountId).Select(z => (Guid?)z.PublicId).FirstOrDefault(), db.Set<BankAccount>().Where(z => z.Id == a.BankAccountId).Select(z => z.Name).FirstOrDefault(),
        db.Set<JournalEntry>().Where(z => z.Id == a.JournalEntryId).Select(z => (Guid?)z.PublicId).FirstOrDefault(), db.Set<JournalEntry>().Where(z => z.Id == a.JournalEntryId).Select(z => z.Number).FirstOrDefault(),
        db.Set<PendingPosting>().Any(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == SourceType && p.SourceId == a.Id && p.ResolvedAt == null),
        a.CarriedFromSettlement != null ? a.CarriedFromSettlement.PublicId : null, a.CarriedFromSettlement != null ? a.CarriedFromSettlement.Number : null));

    private static readonly IReadOnlyDictionary<string, Expression<Func<Advance, object?>>> SortFields = new Dictionary<string, Expression<Func<Advance, object?>>>(StringComparer.Ordinal)
    {
        ["advanceDate"] = a => a.AdvanceDate,
        ["number"] = a => a.Number,
        ["driver"] = a => a.Driver.FullName,
        ["amount"] = a => a.Amount,
    };

    private IQueryable<Advance> Filter(AdvanceListQuery q)
    {
        var query = CashSupport.WithStatus(db.Set<Advance>().AsNoTracking(), q.Status);
        if (!string.IsNullOrWhiteSpace(q.Driver))
        {
            var g = support.Decode<Driver>(q.Driver) ?? throw Fail.Field("driver", ErrorCodes.ValidationFormat, new FieldRef("driver"));
            query = query.Where(a => a.Driver.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(a => a.AdvanceDate >= from);
        if (q.To is DateOnly to) query = query.Where(a => a.AdvanceDate <= to);
        if (CashSupport.Method(q.Method) is { } m) query = query.Where(a => a.PaymentMethod == m);
        query = (q.Settled?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "yes" or "true" => query.Where(a => a.SettlementId != null),
            "no" or "false" => query.Where(a => a.SettlementId == null),
            _ => throw Fail.Field("settled", ErrorCodes.ValidationFormat, new FieldRef("settled")),
        };
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(a => a.Number.Contains(text) || (a.Reference != null && a.Reference.Contains(text)));
        return query;
    }

    public async Task<AdvancePage> ListAsync(AdvanceListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = Filter(q);
        var paging = new PageRequest(q.Page, q.PageSize);
        var totals = await query.GroupBy(_ => 1).Select(g => new CashListTotals(g.Count(), g.Sum(a => a.Status == CashCodes.Active ? a.Amount : 0), g.Count(a => a.Status == CashCodes.Active))).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? new CashListTotals(0, 0);
        var rows = await Rows(MasterDataSupport.Sort(query, q.Sort, SortFields, "-advanceDate").ThenByDescending(a => a.Id).Skip(paging.Skip).Take(paging.SafePageSize))
            .ToListAsync(ct).ConfigureAwait(false);
        return new AdvancePage(new PagedResult<AdvanceResponse>(rows.Select(ToResponse).ToList(), paging.SafePage, paging.SafePageSize, totals.Count), totals);
    }

    public async Task<GeneratedFile> ExportAsync(AdvanceListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var rows = await Rows(MasterDataSupport.Sort(Filter(q), q.Sort, SortFields, "-advanceDate").ThenByDescending(a => a.Id).Take(SheetData.MaxRows)).ToListAsync(ct).ConfigureAwait(false);
        string[] headers = cash.En
            ? ["Number", "Date", "Driver", "Amount", "Method", "Cash box / bank", "Reference", "Settlement", "Status", "Notes"]
            : ["الرقم", "التاريخ", "السائق", "المبلغ", "طريقة الدفع", "الصندوق / البنك", "المرجع", "التسوية", "الحالة", "ملاحظات"];
        var data = rows.Select(x => (IReadOnlyList<object?>)[x.A.Number, x.A.AdvanceDate, x.DriverName, x.A.Amount, x.A.PaymentMethod, x.BoxName ?? x.BankName, x.A.Reference,
            x.SettlementNumber, x.A.Status, x.A.Notes]).ToList();
        return await CashExport.FileAsync("advances", format, headers, data, sheets, exporter, ct).ConfigureAwait(false);
    }

    public async Task<AdvanceDetails> GetAsync(string token, CancellationToken ct)
    {
        var a = await support.FindAsync<Advance>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(a.Id, ct).ConfigureAwait(false);
    }

    internal async Task<AdvanceDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<Advance>().AsNoTracking().Where(a => a.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        return new AdvanceDetails(ToResponse(row), await cash.WordsAsync(row.A.Amount, ct).ConfigureAwait(false), await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }

    internal AdvanceResponse ToResponse(Row x) => new(support.Token<Advance>(x.A.PublicId), x.A.Number, new Ref(support.Token<Driver>(x.DriverPid), x.DriverName),
        x.A.AdvanceDate, x.A.Amount, x.A.PaymentMethod, cash.Account(x.BoxPid, x.BoxName, x.BankPid, x.BankName), x.A.Reference, x.A.Notes, x.A.Status,
        x.SettlementPid is Guid s ? new DocRef(support.Token<Settlement>(s), x.SettlementNumber) : null, cash.Entry(x.EntryPid, x.EntryNumber), x.Pending, x.A.VoidReason,
        DateTime.SpecifyKind(x.A.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.A.RowVersion),
        x.CarriedPid is Guid cf ? new DocRef(support.Token<Settlement>(cf), x.CarriedNumber) : null);
}

/// <summary>Pending-posting retries for advances.</summary>
public sealed class AdvancePostingHandler(IAppDbContext db, IUrlTokenService tokens, AdvanceService advances, CashSupport cash) : CashPostingHandler<Advance>(db, tokens)
{
    public override string SourceType => AdvanceService.SourceType;

    protected override string VoidCode => ErrorCodes.AdvanceIsVoid;

    protected override async Task<PostingDocument?> EntryAsync(Advance d, CancellationToken ct) =>
        advances.BuildEntry(d, await cash.AccountIdAsync(d.CashBoxId, d.BankAccountId, ct).ConfigureAwait(false));
}
