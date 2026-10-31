using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Audit;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Accounting.Domain.Acc;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Budgets;

/// <summary>
/// Budgets (الموازنة; revision 6 part B, owner change request 15 Oct 2026): a company's budget of a fiscal year (calendar year) with lines per
/// revenue or expense account (posting or parent account) and optional cost centre, twelve monthly amounts in the natural direction of the
/// account. Created empty (typed in), copied from the previous year's actuals with an optional percentage, or filled by Excel import
/// (<see cref="BudgetImportService"/>). Draft → Approved (acc.budget.approve) → back to Draft; only one Approved budget per company and year,
/// and only it is compared with the actuals. Approved budgets cannot be changed or deleted. Audited; the history shows the budget's and its
/// lines' changes.
/// Owner request 26 Oct 2026 (DT-265): the budget screen shows every posting account at once (all types, balance-sheet accounts too); a
/// save keeps only lines with an amount. The cost centre is only a filter of that screen — the budget stays company-wide; lines per cost
/// centre or on a parent account made earlier (copy by cost centre, import) are kept and still counted.
/// </summary>
public sealed class BudgetService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, DirectService.Accounting.Contracts.Posting.IAccountingPosting posting, IChangeContext changes,
    IAuditDescriber describer)
{
    public const string EntityType = "ACC.Budget";
    public const string LineEntityType = "ACC.BudgetLine";
    public const int NameMax = 200;
    public const int MaxLines = 2000;
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const string Revenue = "Revenue";
    public const string Expense = "Expense";

    /// <summary>Every account type may be budgeted (DT-265); Budget vs Actual compares revenue and expenses only.</summary>
    public static bool IsBudgetType(string accountType) => AccountingCodes.AccountTypes.Contains(accountType);

    // ------------------------------------------------------------ read

    public async Task<IReadOnlyList<BudgetSummary>> ListAsync(int? year, CancellationToken ct)
    {
        var q = db.Set<Budget>().AsNoTracking();
        if (year is int y) q = q.Where(b => b.FiscalYear == y);
        var rows = await q.OrderByDescending(b => b.FiscalYear).ThenBy(b => b.Name)
            .Select(b => new
            {
                b.PublicId, b.FiscalYear, b.Name, b.Status, b.ApprovedAt, b.ApprovedBy, Updated = b.UpdatedAt ?? b.CreatedAt, b.RowVersion,
                Lines = b.Lines.Count(),
                Revenue = b.Lines.Where(l => l.Account.AccountType == Revenue).Sum(l => (decimal?)l.Total) ?? 0,
                Expense = b.Lines.Where(l => l.Account.AccountType == Expense).Sum(l => (decimal?)l.Total) ?? 0,
            })
            .ToListAsync(ct).ConfigureAwait(false);
        var names = await UserNamesAsync(rows.Select(r => r.ApprovedBy), ct).ConfigureAwait(false);
        return rows.Select(r => new BudgetSummary(support.Token<Budget>(r.PublicId), r.FiscalYear, r.Name, r.Status, r.Lines, r.Revenue, r.Expense,
            Utc(r.ApprovedAt), r.ApprovedBy is long u ? names.GetValueOrDefault(u) : null, DateTime.SpecifyKind(r.Updated, DateTimeKind.Utc),
            Convert.ToBase64String(r.RowVersion))).ToList();
    }

    /// <summary>The accounts a line may use: active revenue and expense accounts, posting and parent, in chart order with their level.</summary>
    public async Task<IReadOnlyList<BudgetAccountOption>> AccountOptionsAsync(CancellationToken ct)
    {
        var all = await db.Set<Account>().AsNoTracking().Select(a => new { a.Id, a.PublicId, a.Code, a.NameAr, a.NameEn, a.AccountType, a.ParentId, a.Status })
            .ToListAsync(ct).ConfigureAwait(false);
        var parentOf = all.ToDictionary(a => a.Id, a => a.ParentId);
        var parents = all.Where(a => a.ParentId is not null).Select(a => a.ParentId!.Value).ToHashSet();
        int Level(long id)
        {
            var level = 1;
            for (var p = parentOf[id]; p is long pid && parentOf.ContainsKey(pid) && level < 64; p = parentOf[pid]) level++;
            return level;
        }
        return all.Where(a => a.Status == AccountingCodes.Active && IsBudgetType(a.AccountType)).OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(a => new BudgetAccountOption(new AccountRef(support.Token<Account>(a.PublicId), a.Code, a.NameAr, a.NameEn), a.AccountType, parents.Contains(a.Id), Level(a.Id)))
            .ToList();
    }

    /// <summary>
    /// The chart for the budget screen (DT-265): every account in chart order with its parent and level; groups (an account with sub-accounts
    /// or not postable) are headings, the others take amounts.
    /// </summary>
    public async Task<IReadOnlyList<BudgetGridAccount>> GridAccountsAsync(CancellationToken ct)
    {
        var all = await db.Set<Account>().AsNoTracking().Select(a => new { a.Id, a.PublicId, a.Code, a.NameAr, a.NameEn, a.AccountType, a.ParentId, a.Status, a.IsPostable })
            .ToListAsync(ct).ConfigureAwait(false);
        var byId = all.ToDictionary(a => a.Id);
        var parents = all.Where(a => a.ParentId is not null).Select(a => a.ParentId!.Value).ToHashSet();
        int Level(long id)
        {
            var level = 1;
            for (var p = byId[id].ParentId; p is long pid && byId.ContainsKey(pid) && level < 64; p = byId[pid].ParentId) level++;
            return level;
        }
        return all.OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(a => new BudgetGridAccount(new AccountRef(support.Token<Account>(a.PublicId), a.Code, a.NameAr, a.NameEn), a.AccountType,
                a.ParentId is long p && byId.TryGetValue(p, out var parent) ? support.Token<Account>(parent.PublicId) : null, Level(a.Id),
                parents.Contains(a.Id) || !a.IsPostable, a.Status == AccountingCodes.Active))
            .ToList();
    }

    /// <summary>
    /// The cost-centre filter of the budget screen (DT-265): the accounts with posted lines for that cost centre in the budget year or the
    /// year before.
    /// </summary>
    public async Task<IReadOnlyList<string>> CostCentreAccountsAsync(string? costCentre, int? year, CancellationToken ct)
    {
        new Validator().Check(year is >= MinYear and <= MaxYear, "year", ErrorCodes.ValidationRange).ThrowIfInvalid();
        if (string.IsNullOrWhiteSpace(costCentre)) throw Fail.Field("costCentre", ErrorCodes.ValidationRequired, new FieldRef("costCentre"));
        var centre = await support.FindAsync<CostCentre>(costCentre, ct).ConfigureAwait(false);
        var from = new DateOnly(year!.Value - 1, 1, 1);
        var to = new DateOnly(year.Value, 12, 31);
        var ids = await db.Set<EntryLine>().AsNoTracking()
            .Where(l => l.CostCentreId == centre.Id && l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to)
            .Select(l => l.Account.PublicId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return ids.Select(support.Token<Account>).ToList();
    }

    public async Task<BudgetResponse> GetAsync(string token, CancellationToken ct)
    {
        var b = await support.FindAsync<Budget>(token, ct).ConfigureAwait(false);
        return await ResponseAsync(b, ct).ConfigureAwait(false);
    }

    private async Task<BudgetResponse> ResponseAsync(Budget b, CancellationToken ct)
    {
        var lines = await db.Set<BudgetLine>().AsNoTracking().Where(l => l.BudgetId == b.Id).Include(l => l.Account).ToListAsync(ct).ConfigureAwait(false);
        var centreIds = lines.Where(l => l.CostCentreId is not null).Select(l => l.CostCentreId!.Value).Distinct().ToList();
        var centres = await db.Set<CostCentre>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(c => centreIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new Ref(support.Token<CostCentre>(c.PublicId), $"{c.Code} – {c.Name}"), ct).ConfigureAwait(false);
        var parents = await db.Set<Account>().AsNoTracking().Where(a => a.ParentId != null).Select(a => a.ParentId!.Value).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var parentSet = parents.ToHashSet();
        var approver = b.ApprovedBy is long u ? (await UserNamesAsync([u], ct).ConfigureAwait(false)).GetValueOrDefault(u) : null;
        var result = lines.OrderBy(l => l.Account.Code, StringComparer.Ordinal).ThenBy(l => l.CostCentreId is long c ? centres.GetValueOrDefault(c)?.Name : string.Empty, StringComparer.Ordinal)
            .Select(l => new BudgetLineResponse(new AccountRef(support.Token<Account>(l.Account.PublicId), l.Account.Code, l.Account.NameAr, l.Account.NameEn), l.Account.AccountType,
                parentSet.Contains(l.AccountId), l.CostCentreId is long c ? centres.GetValueOrDefault(c) : null, l.Months(), l.Total))
            .ToList();
        return new BudgetResponse(support.Token<Budget>(b.PublicId), b.FiscalYear, b.Name, b.Status, Utc(b.ApprovedAt), approver, result,
            result.Where(l => l.AccountType == Revenue).Sum(l => l.Total), result.Where(l => l.AccountType == Expense).Sum(l => l.Total), Convert.ToBase64String(b.RowVersion));
    }

    /// <summary>History of the budget and of its lines (each line event names its account and cost centre), newest first.</summary>
    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var b = await support.FindAsync<Budget>(token, ct).ConfigureAwait(false);
        var lines = await db.Set<BudgetLine>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(l => l.BudgetId == b.Id)
            .Select(l => new { l.Id, l.Account.Code, l.Account.NameAr, l.Account.NameEn, l.CostCentreId })
            .ToListAsync(ct).ConfigureAwait(false);
        var centreCodes = await db.Set<CostCentre>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .ToDictionaryAsync(c => c.Id, c => c.Code, ct).ConfigureAwait(false);
        var lineIds = lines.Select(l => l.Id).ToList();
        var companyId = ctx.CompanyId;
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && ((a.EntityType == EntityType && a.EntityId == b.Id) || (a.EntityType == LineEntityType && lineIds.Contains(a.EntityId!.Value))))
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(500)
            .Select(a => new AuditRow(a.Id, a.OccurredAt, a.UserId, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues))
            .ToListAsync(ct).ConfigureAwait(false);
        var described = await describer.DescribeAsync(rows, new DescribeOptions(CreateValues: true), ct).ConfigureAwait(false);
        var en = ctx.Language == "en";
        var label = lines.ToDictionary(l => l.Id, l => $"{l.Code} {(en ? l.NameEn ?? l.NameAr : l.NameAr)}" + (l.CostCentreId is long c && centreCodes.TryGetValue(c, out var code) ? $" · {code}" : string.Empty));
        return described.Where(h => !h.IsTechnicalOnly)
            .Select(h => new HistoryEntry(DateTime.SpecifyKind(h.Row.OccurredAt, DateTimeKind.Utc), h.User, h.Row.Action, h.Reason, h.Changes,
                h.Row.EntityType == LineEntityType && h.Row.EntityId is long id && label.TryGetValue(id, out var name) ? $"{h.Sentence} — {name}" : h.Sentence,
                h.ActionLabel, h.Lines.Count == 0 ? null : h.Lines, h.Links.Count == 0 ? null : h.Links))
            .ToList();
    }

    // ------------------------------------------------------------ write

    public async Task<BudgetResponse> CreateAsync(CreateBudgetRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = TextNormalizer.CleanText(r.Name);
        new Validator()
            .Check(r.FiscalYear is >= MinYear and <= MaxYear, "fiscalYear", ErrorCodes.ValidationRange)
            .Require("name", name)
            .MaxLength("name", name, NameMax)
            .Check(r.IncreasePercent is null or (>= -100 and <= 1000), "increasePercent", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        await EnsureNameFreeAsync(r.FiscalYear, name!, null, ct).ConfigureAwait(false);
        var lines = r.CopyActuals == true
            ? await FromActualsAsync(r.FiscalYear - 1, r.IncreasePercent ?? 0, r.ByCostCentre == true, ct).ConfigureAwait(false)
            : [];
        var budget = new Budget { FiscalYear = r.FiscalYear, Name = name!, Status = BudgetStatus.Draft };
        foreach (var (accountId, centreId, months) in lines)
        {
            var line = new BudgetLine { Budget = budget, AccountId = accountId, CostCentreId = centreId };
            line.SetMonths(months);
            budget.Lines.Add(line);
        }
        db.Set<Budget>().Add(budget);
        if (r.CopyActuals == true) changes.SetAction(budget, "Create", $"Copied from the actuals of {r.FiscalYear - 1}" + (r.IncreasePercent is decimal p && p != 0 ? $" {p:+0.##;-0.##}%" : string.Empty));
        await SaveAsync(budget, ct).ConfigureAwait(false);
        return await ResponseAsync(budget, ct).ConfigureAwait(false);
    }

    /// <summary>Saves a draft: name and the full set of lines (matched by account and cost centre; missing lines are removed).</summary>
    public async Task<BudgetResponse> UpdateAsync(string token, BudgetRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var budget = await support.FindAsync<Budget>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(budget, r.RowVersion);
        EnsureDraft(budget);
        var name = TextNormalizer.CleanText(r.Name);
        new Validator().Require("name", name).MaxLength("name", name, NameMax).ThrowIfInvalid();
        if (name != budget.Name) await EnsureNameFreeAsync(budget.FiscalYear, name!, budget.Id, ct).ConfigureAwait(false);
        var lines = await ResolveLinesAsync(r.Lines ?? [], ct).ConfigureAwait(false);
        budget.Name = name!;
        var linesChanged = await ReplaceLinesAsync(budget, lines, "Removed from the budget", ct).ConfigureAwait(false);
        // A change of lines only still moves the budget's row version, so a second editor working from the old version is refused.
        if (linesChanged && db.Set<Budget>().Entry(budget).State == EntityState.Unchanged) db.Set<Budget>().Entry(budget).Property(b => b.UpdatedAt).IsModified = true;
        await SaveAsync(budget, ct).ConfigureAwait(false);
        return await ResponseAsync(budget, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the lines of a draft (save, import): same account + cost centre = the same line (amounts updated), new ones added, the rest
    /// removed. Returns whether anything changed.
    /// </summary>
    internal async Task<bool> ReplaceLinesAsync(Budget budget, IReadOnlyList<(long AccountId, long? CostCentreId, decimal[] Months)> lines, string removeReason, CancellationToken ct)
    {
        var existing = await db.Set<BudgetLine>().Where(l => l.BudgetId == budget.Id).ToListAsync(ct).ConfigureAwait(false);
        var byKey = existing.ToDictionary(l => (l.AccountId, l.CostCentreId));
        var changed = false;
        foreach (var (accountId, centreId, months) in lines)
        {
            if (byKey.Remove((accountId, centreId), out var line))
            {
                if (line.Months().SequenceEqual(months)) continue;
            }
            else
            {
                line = new BudgetLine { BudgetId = budget.Id, AccountId = accountId, CostCentreId = centreId };
                db.Set<BudgetLine>().Add(line);
            }
            line.SetMonths(months);
            changed = true;
        }
        foreach (var gone in byKey.Values)
        {
            gone.DeleteReason = removeReason;
            db.Set<BudgetLine>().Remove(gone);
            changed = true;
        }
        return changed;
    }

    public async Task<BudgetResponse> ApproveAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var budget = await support.FindAsync<Budget>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(budget, rowVersion);
        if (budget.Status == BudgetStatus.Approved) return await ResponseAsync(budget, ct).ConfigureAwait(false);
        if (!await db.Set<BudgetLine>().AnyAsync(l => l.BudgetId == budget.Id, ct).ConfigureAwait(false)) throw Fail.Rule(ErrorCodes.BudgetEmpty);
        await EnsureNoOtherApprovedAsync(budget, ct).ConfigureAwait(false);
        budget.Status = BudgetStatus.Approved;
        budget.ApprovedAt = DateTime.UtcNow;
        budget.ApprovedBy = ctx.UserId;
        changes.SetAction(budget, "Approve");
        try
        {
            await SaveAsync(budget, ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another budget of the year was approved at the same moment (UX_Budgets_CompanyId_FiscalYear_Approved).
            db.ChangeTracker.Clear();
            await EnsureNoOtherApprovedAsync(budget, ct).ConfigureAwait(false);
            throw;
        }
        return await ResponseAsync(budget, ct).ConfigureAwait(false);
    }

    /// <summary>Returns an approved budget to draft (it is then no longer compared with the actuals and can be changed).</summary>
    public async Task<BudgetResponse> ReturnToDraftAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var budget = await support.FindAsync<Budget>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(budget, rowVersion);
        if (budget.Status != BudgetStatus.Approved) throw Fail.Rule(ErrorCodes.BudgetNotApproved);
        budget.Status = BudgetStatus.Draft;
        budget.ApprovedAt = null;
        budget.ApprovedBy = null;
        changes.SetAction(budget, "Reopen", "Returned to draft");
        await SaveAsync(budget, ct).ConfigureAwait(false);
        return await ResponseAsync(budget, ct).ConfigureAwait(false);
    }

    /// <summary>Deletes a draft budget and its lines (soft delete).</summary>
    public async Task DeleteAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var budget = await support.FindAsync<Budget>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(budget, rowVersion);
        EnsureDraft(budget);
        await db.InTransactionAsync(async c =>
        {
            foreach (var line in await db.Set<BudgetLine>().Where(l => l.BudgetId == budget.Id).ToListAsync(c).ConfigureAwait(false))
            {
                line.DeleteReason = "Budget deleted";
                db.Set<BudgetLine>().Remove(line);
            }
            budget.DeleteReason = "Budget deleted";
            db.Set<Budget>().Remove(budget);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return 0;
        }, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ rules

    internal static void EnsureDraft(Budget budget)
    {
        if (budget.Status != BudgetStatus.Draft) throw Fail.Rule(ErrorCodes.BudgetApproved);
    }

    private async Task SaveAsync(Budget budget, CancellationToken ct) =>
        await db.InTransactionAsync(async c => await db.SaveChangesAsync(c).ConfigureAwait(false), ct).ConfigureAwait(false);

    private async Task EnsureNameFreeAsync(int year, string name, long? self, CancellationToken ct)
    {
        if (await db.Set<Budget>().AnyAsync(b => b.FiscalYear == year && b.Name == name && b.Id != self, ct).ConfigureAwait(false))
            throw Fail.Field("name", ErrorCodes.BudgetNameTaken, name, year);
    }

    private async Task EnsureNoOtherApprovedAsync(Budget budget, CancellationToken ct)
    {
        var other = await db.Set<Budget>().AsNoTracking().Where(b => b.FiscalYear == budget.FiscalYear && b.Status == BudgetStatus.Approved && b.Id != budget.Id)
            .Select(b => b.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (other is not null) throw Fail.Rule(ErrorCodes.BudgetApprovedExists, other, budget.FiscalYear);
    }

    /// <summary>The company's accounts for budget rules: id → (code, type, parent).</summary>
    internal async Task<Dictionary<long, (Guid PublicId, string Code, string Type, long? ParentId)>> AccountsAsync(CancellationToken ct) =>
        await db.Set<Account>().AsNoTracking().ToDictionaryAsync(a => a.Id, a => (a.PublicId, a.Code, a.AccountType, a.ParentId), ct).ConfigureAwait(false);

    /// <summary>
    /// Validates the lines of a save: account (posting or parent), cost centre, twelve amounts (currency decimals, may be negative), no account
    /// twice for the same cost centre, no line on an account whose parent also has a line for that cost centre. Lines whose twelve amounts are
    /// all zero are not kept (DT-265: only lines with amounts are stored).
    /// </summary>
    private async Task<List<(long AccountId, long? CostCentreId, decimal[] Months)>> ResolveLinesAsync(IReadOnlyList<BudgetLineRequest> input, CancellationToken ct)
    {
        if (input.Count > MaxLines) throw Fail.Field("lines", ErrorCodes.ValidationRange, new FieldRef("lines"));
        var decimals = await posting.CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var accounts = await AccountsAsync(ct).ConfigureAwait(false);
        var byPublic = accounts.ToDictionary(a => a.Value.PublicId, a => a.Key);
        var centres = await db.Set<CostCentre>().AsNoTracking().ToDictionaryAsync(c => c.PublicId, c => c.Id, ct).ConfigureAwait(false);
        var result = new List<(long, long?, decimal[])>(input.Count);
        var origin = new List<int>(input.Count);   // the request index of each kept line (all-zero lines are left out)
        for (var i = 0; i < input.Count; i++)
        {
            var l = input[i];
            var field = $"lines[{i}]";
            var accountId = support.Decode<Account>(l.AccountId) is Guid g && byPublic.TryGetValue(g, out var a) ? a
                : throw Fail.Field(field + ".accountId", string.IsNullOrEmpty(l.AccountId) ? ErrorCodes.ValidationRequired : ErrorCodes.ValidationFormat, new FieldRef(field + ".accountId"));
            if (!IsBudgetType(accounts[accountId].Type)) throw Fail.Field(field + ".accountId", ErrorCodes.BudgetAccountType, accounts[accountId].Code);
            long? centreId = null;
            if (!string.IsNullOrEmpty(l.CostCentreId))
                centreId = support.Decode<CostCentre>(l.CostCentreId) is Guid cg && centres.TryGetValue(cg, out var c) ? c
                    : throw Fail.Field(field + ".costCentreId", ErrorCodes.ValidationFormat, new FieldRef(field + ".costCentreId"));
            var months = l.Months?.ToArray() ?? [];
            if (months.Length != 12 || months.Any(m => !BudgetMath.IsValidAmount(m, decimals))) throw Fail.Field(field + ".months", ErrorCodes.ValidationRange, new FieldRef(field + ".months"));
            if (months.All(m => m == 0)) continue;
            result.Add((accountId, centreId, months));
            origin.Add(i);
        }
        if (BudgetMath.FindConflict(result.Select(r => (r.Item1, r.Item2)).ToList(), id => accounts.TryGetValue(id, out var x) ? x.ParentId : null) is { } conflict)
        {
            var field = $"lines[{origin[conflict.Index]}].accountId";
            throw conflict.AncestorId is long anc
                ? Fail.Field(field, ErrorCodes.BudgetOverlap, accounts[result[conflict.Index].Item1].Code, accounts[anc].Code)
                : Fail.Field(field, ErrorCodes.BudgetDuplicateLine, accounts[result[conflict.Index].Item1].Code);
        }
        return result;
    }

    /// <summary>
    /// Lines from the actuals of a year: per revenue/expense account (and cost centre when asked) and month, revenue = credit − debit, expense =
    /// debit − credit, times (1 + percent ÷ 100), rounded to the currency decimals; accounts without movement are left out.
    /// </summary>
    internal async Task<List<(long AccountId, long? CostCentreId, decimal[] Months)>> FromActualsAsync(int year, decimal percent, bool byCostCentre, CancellationToken ct)
    {
        var decimals = await posting.CurrencyDecimalsAsync(ct).ConfigureAwait(false);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var sums = await db.Set<EntryLine>().AsNoTracking()
            .Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && (l.Account.AccountType == Revenue || l.Account.AccountType == Expense))
            .GroupBy(l => new { l.AccountId, CostCentreId = byCostCentre ? l.CostCentreId : null, l.JournalEntry.EntryDate.Month, l.Account.AccountType })
            .Select(g => new { g.Key.AccountId, g.Key.CostCentreId, g.Key.Month, g.Key.AccountType, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .ToListAsync(ct).ConfigureAwait(false);
        var lines = sums.GroupBy(s => (s.AccountId, s.CostCentreId))
            .Select(g =>
            {
                var months = new decimal[12];
                foreach (var s in g) months[s.Month - 1] = BudgetMath.Increase(BudgetMath.Natural(s.AccountType, s.Debit, s.Credit), percent, decimals);
                return (g.Key.AccountId, g.Key.CostCentreId, months);
            })
            .Where(l => l.months.Any(m => m != 0))
            .ToList();
        if (lines.Count == 0) throw Fail.Rule(ErrorCodes.BudgetNoActuals, year);
        return lines;
    }

    private async Task<Dictionary<long, string>> UserNamesAsync(IEnumerable<long?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Set<User>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(u => list.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct).ConfigureAwait(false);
    }

    private static DateTime? Utc(DateTime? d) => d is DateTime x ? DateTime.SpecifyKind(x, DateTimeKind.Utc) : null;
}
