using System.Linq.Expressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// Driver settlements (FR-076 … FR-082, 8.5, 9.10): the driver's open own-fleet trips and open advances up to a date are pre-selected
/// (a subset may be chosen, BR-031); deductions (shortage — suggested from flagged trips —, fine, other); net = gross − advances −
/// deductions ≥ 0; confirming numbers the settlement, links and locks the items and posts Dr driver payable (trips whose wage was
/// accrued) / driver wage expense (the others, BR-031a), Cr driver advances, driver deductions and cash/bank (net). Void releases the
/// items and reverses the entry. Revision 6 (DT-228): when advances and deductions exceed the wages, the driver repays part or all of the
/// shortfall with the settlement (Dr cash / bank, Cr driver advances, same STL entry); an unpaid rest becomes a new open advance
/// ("Carried", no money moved, kept on driver advances by the same entry) for the next settlement. Void reverses it and voids that advance.
/// </summary>
public sealed class SettlementService(IAppDbContext db, MasterDataSupport support, CashSupport cash, IChangeContext changes, IRecordLockGuard locks, IPeriodGuard periods,
    INumberingService numbering, PostingEngine posting, TripService trips)
{
    public const string EntityType = "TRN.Settlement";
    public const string SourceType = "Settlement";
    private static readonly string[] DeductionTypes = [CashCodes.Shortage, CashCodes.Fine, CashCodes.Other];

    // ============================================================ open items (FR-076, FR-077)

    private IQueryable<Trip> OpenTrips(long driverId, DateOnly upTo) =>
        db.Set<Trip>().Where(t => t.DriverId == driverId && t.Status == TripService.Active && t.SettlementId == null && t.PartnerId == null && t.TripDate <= upTo);

    private IQueryable<Advance> OpenAdvances(long driverId, DateOnly upTo) =>
        db.Set<Advance>().Where(a => a.DriverId == driverId && a.Status == CashCodes.Active && a.SettlementId == null && a.AdvanceDate <= upTo);

    internal static IQueryable<SettlementTripItem> TripItems(IQueryable<Trip> q) => q.OrderBy(t => t.TripDate).ThenBy(t => t.Id).Select(t => new SettlementTripItem(
        t.PublicId.ToString(), t.ReceiptNo, t.TripDate, t.LoadingSite.Name + " ← " + t.UnloadingSite.Name, t.Material.Name, t.WageQty, t.DriverRate, t.DriverWage,
        t.IsShortageFlagged, t.ShortageWeight));

    internal static IQueryable<SettlementAdvanceItem> AdvanceItems(IQueryable<Advance> q) => q.OrderBy(a => a.AdvanceDate).ThenBy(a => a.Id)
        .Select(a => new SettlementAdvanceItem(a.PublicId.ToString(), a.Number, a.AdvanceDate, a.Amount));

    /// <summary>Public ids → URL tokens for the items read above (tokens cannot be computed inside the query).</summary>
    internal List<SettlementTripItem> Tokens(List<SettlementTripItem> items) => items.Select(i => i with { Id = support.Token<Trip>(Guid.Parse(i.Id)) }).ToList();

    internal List<SettlementAdvanceItem> Tokens(List<SettlementAdvanceItem> items) => items.Select(i => i with { Id = support.Token<Advance>(Guid.Parse(i.Id)) }).ToList();

    public async Task<SettlementPreview> PreviewAsync(string? driverToken, DateOnly? date, CancellationToken ct)
    {
        var driver = await support.FindAsync<Driver>(driverToken, ct).ConfigureAwait(false);
        var upTo = date ?? await support.TodayAsync(ct).ConfigureAwait(false);
        var tripItems = Tokens(await TripItems(OpenTrips(driver.Id, upTo).AsNoTracking()).ToListAsync(ct).ConfigureAwait(false));
        var advanceItems = Tokens(await AdvanceItems(OpenAdvances(driver.Id, upTo).AsNoTracking()).ToListAsync(ct).ConfigureAwait(false));
        var suggestions = await SuggestionsAsync(OpenTrips(driver.Id, upTo), ct).ConfigureAwait(false);
        var gross = tripItems.Sum(t => t.DriverWage);
        var advances = advanceItems.Sum(a => a.Amount);
        return new SettlementPreview(new Ref(support.Token<Driver>(driver.PublicId), driver.FullName), driver.PlateNumber, upTo, tripItems, advanceItems, suggestions,
            gross, advances, gross - advances);
    }

    /// <summary>
    /// Shortage suggestions (FR-078, DT-96): for flagged trips, the shortage beyond the company tolerance × the trip's price per ton,
    /// half-up; the user may change or drop them.
    /// </summary>
    private async Task<List<SuggestedDeduction>> SuggestionsAsync(IQueryable<Trip> open, CancellationToken ct)
    {
        var s = await trips.SettingsAsync(ct).ConfigureAwait(false);
        var flagged = await open.AsNoTracking().Where(t => t.IsShortageFlagged && t.ShortageWeight > 0)
            .Select(t => new { t.PublicId, t.ReceiptNo, t.LoadedWeight, Shortage = t.ShortageWeight!.Value, t.CustomerPrice }).ToListAsync(ct).ConfigureAwait(false);
        return flagged.Select(t =>
        {
            var excess = Math.Max(0, t.Shortage - Math.Round(t.LoadedWeight * s.ShortageTolerancePct / 100m, 3, MidpointRounding.AwayFromZero));
            return new SuggestedDeduction(support.Token<Trip>(t.PublicId), t.ReceiptNo, t.Shortage, excess, Money.Round(excess * t.CustomerPrice, s.Decimals));
        }).Where(x => x.Amount > 0).ToList();
    }

    // ============================================================ confirm (FR-079, FR-080)

    public async Task<(SettlementDetails Value, bool Created)> CreateAsync(SettlementRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Settlement>(clientId, ct).ConfigureAwait(false) is { } again) return (await DetailsAsync(again.Id, ct).ConfigureAwait(false), false);
        new Validator().Check(!string.IsNullOrWhiteSpace(r.DriverId), "driverId", ErrorCodes.ValidationRequired).ThrowIfInvalid();
        var date = await cash.DateAsync(r.SettlementDate, "settlementDate", ct).ConfigureAwait(false);
        // An archived driver (left the company) can still receive the final settlement of open items (DT-97).
        var driver = await cash.PartyAsync<Driver>("driverId", r.DriverId, ct).ConfigureAwait(false);
        var (_, decimals) = await cash.CurrencyAsync(ct).ConfigureAwait(false);
        var deductions = Deductions(r.Deductions, decimals);
        var money = await cash.ChooseAsync(r.PaymentMethod, r.CashBoxId, r.BankAccountId, r.Reference, ct).ConfigureAwait(false);

        var stl = new Settlement
        {
            DriverId = driver.Id, SettlementDate = date, PaymentMethod = money.Method, CashBoxId = money.CashBoxId, BankAccountId = money.BankAccountId,
            Reference = money.Reference, Status = CashCodes.Active,
        };
        if (clientId is Guid id) stl.PublicId = id;
        Settlement? raced = null;
        try
        {
            await db.InTransactionAsync(async c =>
            {
                await posting.EnsurePostingReadyAsync(c).ConfigureAwait(false);
                await periods.EnsureOpenAsync(date, c).ConfigureAwait(false);
                // The settlement number serialises settlements; the open items are read under it (BR-031: one active settlement per item).
                stl.Number = await numbering.NextAsync(Configuration.DocumentTypes.Settlement, c).ConfigureAwait(false);
                var tripList = await Pick(OpenTrips(driver.Id, date), r.TripIds, "tripIds", t => t.PublicId, support.Decode<Trip>, c).ConfigureAwait(false);
                var advanceList = await Pick(OpenAdvances(driver.Id, date), r.AdvanceIds, "advanceIds", a => a.PublicId, support.Decode<Advance>, c).ConfigureAwait(false);
                if (tripList.Count == 0 && advanceList.Count == 0) throw Fail.Rule(ErrorCodes.SettlementEmpty);
                stl.GrossWages = tripList.Sum(t => t.DriverWage);
                stl.AdvancesTotal = advanceList.Sum(a => a.Amount);
                stl.DeductionsTotal = deductions.Sum(d => d.Amount);
                var net = stl.GrossWages - stl.AdvancesTotal - stl.DeductionsTotal;
                // 8.5 / FR-079: never below zero — a shortfall is repaid by the driver (all or part) and the rest carried (DT-228).
                if (net < 0)
                {
                    if (r.RepaidAmount is not decimal repaid) throw Fail.Rule(ErrorCodes.SettlementNetNegative, Money.Format(net));
                    if (repaid <= 0 || repaid > -net || decimal.Round(repaid, decimals) != repaid)
                        throw Fail.RuleOn("repaidAmount", ErrorCodes.SettlementRepaymentRange, Money.Format(-net));
                    stl.RepaidAmount = repaid;
                    stl.CarriedForward = -net - repaid;
                }
                else if (r.RepaidAmount is decimal extra && extra != 0) throw Fail.RuleOn("repaidAmount", ErrorCodes.SettlementRepaymentNotNeeded);
                stl.NetPayable = Math.Max(net, 0);
                db.Set<Settlement>().Add(stl);
                if (await support.SaveCreateAsync<Settlement>(clientId, c).ConfigureAwait(false) is { } other) throw new RacedCreate(other);
                if (stl.CarriedForward > 0)
                {
                    // The unpaid rest: a new open advance of the driver, numbered like any advance, dated with the settlement.
                    var carried = new Advance
                    {
                        DriverId = driver.Id, AdvanceDate = date, Amount = stl.CarriedForward, PaymentMethod = CashCodes.Carried, Status = CashCodes.Active,
                        CarriedFromSettlementId = stl.Id, Number = await numbering.NextAsync(Configuration.DocumentTypes.Advance, c).ConfigureAwait(false),
                        Notes = (cash.En ? "Carried from settlement " : "مرحّلة من التسوية ") + stl.Number,
                    };
                    db.Set<Advance>().Add(carried);
                    changes.SetAction(carried, "CarriedForward", stl.Number);
                }
                foreach (var d in deductions)
                    db.Set<SettlementDeduction>().Add(new SettlementDeduction { SettlementId = stl.Id, DeductionType = d.Type, Amount = d.Amount, Note = d.Note });
                foreach (var t in tripList)
                {
                    t.SettlementId = stl.Id;
                    changes.SetAction(t, "Settled", stl.Number);
                }
                foreach (var a in advanceList)
                {
                    a.SettlementId = stl.Id;
                    changes.SetAction(a, "Settled", stl.Number);
                }
                await db.SaveChangesAsync(c).ConfigureAwait(false);
                // Trips whose wage was accrued (entry posted or waiting) are settled against driver payable; the others against expense.
                var ids = tripList.Select(t => t.Id).ToList();
                var pending = await db.Set<PendingPosting>().Where(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == TripService.SourceType
                    && ids.Contains(p.SourceId) && p.ResolvedAt == null).Select(p => p.SourceId).ToListAsync(c).ConfigureAwait(false);
                var posted = await db.Set<JournalEntry>().Where(e => e.SourceModule == AccountingCodes.ModuleTrn && e.SourceType == TripService.SourceType && ids.Contains(e.SourceId!.Value)
                    && e.Status == AccountingCodes.Posted && e.ReversesEntryId == null).Select(e => e.Id).ToListAsync(c).ConfigureAwait(false);
                var accruedGross = tripList.Where(t => (t.JournalEntryId is long j && posted.Contains(j)) || pending.Contains(t.Id)).Sum(t => t.DriverWage);
                var request = BuildEntry(stl, money.AccountId, accruedGross);
                if (request is not null)
                {
                    var entry = await posting.PostOrPendAsync(request, c).ConfigureAwait(false);
                    stl.JournalEntryId = entry?.Id;
                    await db.SaveChangesAsync(c).ConfigureAwait(false);
                }
            }, ct).ConfigureAwait(false);
        }
        catch (RacedCreate rc)
        {
            db.ChangeTracker.Clear();
            raced = rc.Existing;
        }
        return raced is not null ? (await DetailsAsync(raced.Id, ct).ConfigureAwait(false), false) : (await DetailsAsync(stl.Id, ct).ConfigureAwait(false), true);
    }

    private sealed class RacedCreate(Settlement existing) : Exception
    {
        public Settlement Existing { get; } = existing;
    }

    private sealed record DeductionInput(string Type, decimal Amount, string? Note);

    /// <summary>Deduction lines (FR-078): type, amount ≥ 0 (zero lines are not kept), note required for "other".</summary>
    private static List<DeductionInput> Deductions(IReadOnlyList<SettlementDeductionRequest>? lines, int decimals)
    {
        var result = new List<DeductionInput>();
        var items = lines ?? [];
        if (items.Count > 50) throw Fail.Field("deductions", ErrorCodes.ValidationRange, new FieldRef("deductions"));
        for (var i = 0; i < items.Count; i++)
        {
            var type = DeductionTypes.FirstOrDefault(t => string.Equals(t, items[i].Type?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw Fail.Field($"deductions[{i}].type", ErrorCodes.ValidationFormat, new FieldRef("type"));
            if (items[i].Amount is not decimal a || a < 0 || decimal.Round(a, decimals) != a) throw Fail.Field($"deductions[{i}].amount", ErrorCodes.ValidationRange, new FieldRef("amount"));
            var note = TextNormalizer.CleanText(items[i].Note);
            if (note is { Length: > 300 }) throw Fail.Field($"deductions[{i}].note", ErrorCodes.ValidationRange, new FieldRef("note"));
            if (type == CashCodes.Other && note is null) throw Fail.Field($"deductions[{i}].note", ErrorCodes.ValidationRequired, new FieldRef("note"));
            if (a > 0) result.Add(new DeductionInput(type, a, note));
        }
        return result;
    }

    /// <summary>All open items (none named) or the named ones, each of which must be open, of this driver and up to the date.</summary>
    private static async Task<List<T>> Pick<T>(IQueryable<T> open, IReadOnlyList<string>? tokens, string field, Func<T, Guid> pid,
        Func<string?, Guid?> decode, CancellationToken ct) where T : class
    {
        var all = await open.ToListAsync(ct).ConfigureAwait(false);
        if (tokens is null) return all;
        var picked = new List<T>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var g = decode(tokens[i]) ?? throw Fail.Field($"{field}[{i}]", ErrorCodes.ValidationFormat, new FieldRef(field));
            var item = all.FirstOrDefault(x => pid(x) == g) ?? throw Fail.RuleOn($"{field}[{i}]", ErrorCodes.SettlementItemUnavailable, i + 1);   // DT-241: its place in the list, never the token
            if (!picked.Contains(item)) picked.Add(item);
        }
        return picked;
    }

    /// <summary>
    /// Settlement entry (8.6): Dr driver payable (accrued wages, with the driver) and driver wage expense (the rest, BR-031a); Cr driver
    /// advances (with the driver), driver deductions, cash / bank (net). Null when every amount is zero. DT-228: Dr cash / bank for the
    /// driver's repayment; driver advances are credited only with what leaves them (settled advances − the carried rest, which stays).
    /// </summary>
    public PostingRequest? BuildEntry(Settlement s, long moneyAccountId, decimal accruedGross)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.GrossWages == 0 && s.AdvancesTotal == 0 && s.DeductionsTotal == 0) return null;
        var party = new PostingParty(AccountingCodes.ModuleTrn, AccountingCodes.PartyDriver, s.DriverId);
        var lines = new List<PostingLine>();
        if (accruedGross > 0) lines.Add(PostingLine.DebitKey(PostingKeys.DriverPayable, accruedGross, party));
        if (s.GrossWages - accruedGross > 0) lines.Add(PostingLine.DebitKey(PostingKeys.DriverWageExpense, s.GrossWages - accruedGross, party));
        // Deductions above the wages can leave more on driver advances than was settled (a debit then).
        var advances = s.AdvancesTotal - s.CarriedForward;
        if (advances > 0) lines.Add(PostingLine.CreditKey(PostingKeys.DriverAdvances, advances, party));
        if (advances < 0) lines.Add(PostingLine.DebitKey(PostingKeys.DriverAdvances, -advances, party));
        if (s.DeductionsTotal > 0) lines.Add(PostingLine.CreditKey(PostingKeys.DriverDeductions, s.DeductionsTotal, party));
        if (s.NetPayable > 0) lines.Add(CashSupport.Credit(moneyAccountId, s.NetPayable));
        if (s.RepaidAmount > 0) lines.Add(CashSupport.Debit(moneyAccountId, s.RepaidAmount));
        return new PostingRequest(s.SettlementDate, (cash.En ? "Driver settlement " : "تسوية سائق ") + s.Number, AccountingCodes.ModuleTrn, SourceType, s.Id, false, lines);
    }

    /// <summary>The accrued part of a stored settlement's gross (pending-posting retries).</summary>
    internal async Task<decimal> AccruedGrossAsync(Settlement s, CancellationToken ct)
    {
        var trips = await db.Set<Trip>().AsNoTracking().Where(t => t.SettlementId == s.Id).Select(t => new { t.Id, t.JournalEntryId, t.DriverWage }).ToListAsync(ct).ConfigureAwait(false);
        var ids = trips.Select(t => t.Id).ToList();
        var pending = await db.Set<PendingPosting>().Where(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == TripService.SourceType && ids.Contains(p.SourceId)
            && p.ResolvedAt == null).Select(p => p.SourceId).ToListAsync(ct).ConfigureAwait(false);
        return trips.Where(t => t.JournalEntryId != null || pending.Contains(t.Id)).Sum(t => t.DriverWage);
    }

    // ============================================================ void (FR-081)

    public async Task<SettlementDetails> VoidAsync(string token, CashVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var stl = await support.FindAsync<Settlement>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, stl.Id, ct).ConfigureAwait(false);
        CashSupport.EnsureActive(stl, ErrorCodes.SettlementIsVoid);
        CashSupport.RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(stl, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(stl.SettlementDate, c).ConfigureAwait(false);
            // DT-228: the carried rest goes with the settlement — unless a later settlement already settled it.
            var carried = await db.Set<Advance>().Where(a => a.CarriedFromSettlementId == stl.Id && a.Status == CashCodes.Active).ToListAsync(c).ConfigureAwait(false);
            foreach (var a in carried)
            {
                if (a.SettlementId is long later)
                    throw Fail.Rule(ErrorCodes.SettlementCarriedSettled, a.Number,
                        await db.Set<Settlement>().Where(x => x.Id == later).Select(x => x.Number).SingleAsync(c).ConfigureAwait(false));
                a.Status = CashCodes.Void;
                a.VoidReason = why;
                a.VoidedBy = cash.UserId;
                a.VoidedAt = DateTime.UtcNow;
                changes.SetAction(a, "SettlementVoided", stl.Number);
            }
            stl.Status = CashCodes.Void;
            stl.VoidReason = why;
            stl.VoidedBy = cash.UserId;
            stl.VoidedAt = DateTime.UtcNow;
            changes.SetAction(stl, "Void", why);
            foreach (var t in await db.Set<Trip>().Where(t => t.SettlementId == stl.Id).ToListAsync(c).ConfigureAwait(false))
            {
                t.SettlementId = null;
                changes.SetAction(t, "SettlementVoided", stl.Number);
            }
            foreach (var a in await db.Set<Advance>().Where(a => a.SettlementId == stl.Id).ToListAsync(c).ConfigureAwait(false))
            {
                a.SettlementId = null;
                changes.SetAction(a, "SettlementVoided", stl.Number);
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await CashPosting.ReverseAsync(db, posting, stl, SourceType, cash.En ? $"Void of settlement {stl.Number}" : $"إلغاء التسوية {stl.Number}", c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await DetailsAsync(stl.Id, ct).ConfigureAwait(false);
    }

    // ============================================================ reads

    internal sealed record Row(Settlement S, Guid DriverPid, string DriverName, Guid? BoxPid, string? BoxName, Guid? BankPid, string? BankName, Guid? EntryPid,
        string? EntryNumber, bool Pending, Guid? CarriedPid = null, string? CarriedNumber = null);

    internal IQueryable<Row> Rows(IQueryable<Settlement> q) => q.Select(s => new Row(s, s.Driver.PublicId, s.Driver.FullName,
        s.CashBox != null ? s.CashBox.PublicId : null, s.CashBox != null ? s.CashBox.Name : null,
        s.BankAccount != null ? s.BankAccount.PublicId : null, s.BankAccount != null ? s.BankAccount.Name : null,
        s.JournalEntry != null ? s.JournalEntry.PublicId : null, s.JournalEntry != null ? s.JournalEntry.Number : null,
        db.Set<PendingPosting>().Any(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == SourceType && p.SourceId == s.Id && p.ResolvedAt == null),
        db.Set<Advance>().Where(a => a.CarriedFromSettlementId == s.Id).Select(a => (Guid?)a.PublicId).FirstOrDefault(),
        db.Set<Advance>().Where(a => a.CarriedFromSettlementId == s.Id).Select(a => a.Number).FirstOrDefault()));

    private static readonly IReadOnlyDictionary<string, Expression<Func<Settlement, object?>>> SortFields = new Dictionary<string, Expression<Func<Settlement, object?>>>(StringComparer.Ordinal)
    {
        ["settlementDate"] = s => s.SettlementDate,
        ["number"] = s => s.Number,
        ["driver"] = s => s.Driver.FullName,
        ["net"] = s => s.NetPayable,
    };

    public async Task<SettlementPage> ListAsync(SettlementListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = CashSupport.WithStatus(db.Set<Settlement>().AsNoTracking(), q.Status);
        if (!string.IsNullOrWhiteSpace(q.Driver))
        {
            var g = support.Decode<Driver>(q.Driver) ?? throw Fail.Field("driver", ErrorCodes.ValidationFormat, new FieldRef("driver"));
            query = query.Where(s => s.Driver.PublicId == g);
        }
        if (q.From is DateOnly from) query = query.Where(s => s.SettlementDate >= from);
        if (q.To is DateOnly to) query = query.Where(s => s.SettlementDate <= to);
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(s => s.Number.Contains(text));
        var paging = new PageRequest(q.Page, q.PageSize);
        var totals = await query.GroupBy(_ => 1).Select(g => new CashListTotals(g.Count(), g.Sum(s => s.Status == CashCodes.Active ? s.NetPayable : 0), g.Count(s => s.Status == CashCodes.Active))).SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? new CashListTotals(0, 0);
        var rows = await Rows(MasterDataSupport.Sort(query, q.Sort, SortFields, "-settlementDate").ThenByDescending(s => s.Id).Skip(paging.Skip).Take(paging.SafePageSize))
            .ToListAsync(ct).ConfigureAwait(false);
        return new SettlementPage(new PagedResult<SettlementResponse>(rows.Select(ToResponse).ToList(), paging.SafePage, paging.SafePageSize, totals.Count), totals);
    }

    public async Task<SettlementDetails> GetAsync(string token, CancellationToken ct)
    {
        var s = await support.FindAsync<Settlement>(token, ct).ConfigureAwait(false);
        return await DetailsAsync(s.Id, ct).ConfigureAwait(false);
    }

    internal async Task<SettlementDetails> DetailsAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<Settlement>().AsNoTracking().Where(s => s.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        var tripItems = Tokens(await TripItems(db.Set<Trip>().AsNoTracking().Where(t => t.SettlementId == id)).ToListAsync(ct).ConfigureAwait(false));
        var advanceItems = Tokens(await AdvanceItems(db.Set<Advance>().AsNoTracking().Where(a => a.SettlementId == id)).ToListAsync(ct).ConfigureAwait(false));
        var deductions = (await db.Set<SettlementDeduction>().AsNoTracking().Where(d => d.SettlementId == id).OrderBy(d => d.Id)
                .Select(d => new { d.PublicId, d.DeductionType, d.Amount, d.Note }).ToListAsync(ct).ConfigureAwait(false))
            .Select(d => new SettlementDeductionResponse(support.Token<SettlementDeduction>(d.PublicId), d.DeductionType, d.Amount, d.Note)).ToList();
        return new SettlementDetails(ToResponse(row), tripItems, advanceItems, deductions, await cash.WordsAsync(row.S.NetPayable, ct).ConfigureAwait(false),
            await support.HistoryAsync(EntityType, id, ct).ConfigureAwait(false));
    }

    internal SettlementResponse ToResponse(Row x) => new(support.Token<Settlement>(x.S.PublicId), x.S.Number, new Ref(support.Token<Driver>(x.DriverPid), x.DriverName),
        x.S.SettlementDate, x.S.GrossWages, x.S.AdvancesTotal, x.S.DeductionsTotal, x.S.NetPayable, x.S.PaymentMethod, cash.Account(x.BoxPid, x.BoxName, x.BankPid, x.BankName),
        x.S.Reference, x.S.Status, cash.Entry(x.EntryPid, x.EntryNumber), x.Pending, x.S.VoidReason, DateTime.SpecifyKind(x.S.CreatedAt, DateTimeKind.Utc),
        Convert.ToBase64String(x.S.RowVersion), x.S.RepaidAmount, x.S.CarriedForward,
        x.CarriedPid is Guid cp ? new DocRef(support.Token<Advance>(cp), x.CarriedNumber!) : null);
}

/// <summary>Pending-posting retries for settlements.</summary>
public sealed class SettlementPostingHandler(IAppDbContext db, IUrlTokenService tokens, SettlementService settlements, CashSupport cash) : CashPostingHandler<Settlement>(db, tokens)
{
    public override string SourceType => SettlementService.SourceType;

    protected override string VoidCode => ErrorCodes.SettlementIsVoid;

    protected override async Task<PostingRequest?> EntryAsync(Settlement d, CancellationToken ct) =>
        settlements.BuildEntry(d, await cash.AccountIdAsync(d.CashBoxId, d.BankAccountId, ct).ConfigureAwait(false), await settlements.AccruedGrossAsync(d, ct).ConfigureAwait(false));
}
