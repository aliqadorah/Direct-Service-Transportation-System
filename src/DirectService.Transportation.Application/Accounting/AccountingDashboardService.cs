using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.ApAr;
using DirectService.Transportation.Application.Budgets;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Ledger;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// The accounting dashboard (لوحة المحاسبة; revision 6 part B, owner change request 15 Oct 2026), first page under Accounting. For a month (default
/// the current one): cash and bank balances, receivables and payables (open documents, as the ageing reports), revenue, expenses and net
/// profit of the month and of the year to date, budget usage of the approved budget; revenue vs expenses of the last 12 months; receivables
/// ageing; and reminders computed from the data, each with a count and the screen it opens. Every part appears only with a permission that
/// opens it; everything is of the company in the address (company switch).
/// </summary>
public sealed class AccountingDashboardService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, LedgerReportService ledger, AgeingService ageing,
    BudgetReportService budgets, TimeProvider clock)
{
    /// <summary>Bills due within this many days are reminded.</summary>
    public const int DueSoonDays = 7;

    /// <summary>A refused deduction within this many days counts as "being entered" for the Supplier deductions reminder.</summary>
    public const int DeductionAttemptDays = 30;

    /// <summary>Permissions that open the dashboard (any of them; each part checks its own).</summary>
    public static readonly string[] Readers =
    [
        PermissionKeys.AcctView, PermissionKeys.JeView, PermissionKeys.JeExport, PermissionKeys.AcctPost, PermissionKeys.AcctApar, PermissionKeys.AcctSetup,
        PermissionKeys.BudgetView, PermissionKeys.ReportsView, PermissionKeys.SettingsManage,
    ];

    private bool Any(params string[] keys) => keys.Any(ctx.HasPermission);

    public async Task<AccountingDashboardResponse> GetAsync(int? year, int? month, CancellationToken ct)
    {
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var y = year ?? today.Year;
        var m = month ?? (y == today.Year ? today.Month : 12);
        new Validator()
            .Check(y is >= 2000 and <= 2100, "year", ErrorCodes.ValidationRange)
            .Check(m is >= 1 and <= 12, "month", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        var monthFrom = new DateOnly(y, m, 1);
        var monthEnd = monthFrom.AddMonths(1).AddDays(-1);
        var monthTo = monthFrom <= today && today <= monthEnd ? today : monthEnd;
        var yearFrom = new DateOnly(y, 1, 1);

        var ledgerReader = Any(PermissionKeys.AcctView, PermissionKeys.ReportsView);
        var ageingReader = Any(PermissionKeys.AcctApar, PermissionKeys.AcctView, PermissionKeys.ReportsView);
        AccountingFigures? figures = null;
        AgeingBuckets? receivablesAgeing = null;
        if (ledgerReader || ageingReader)
        {
            decimal? cash = null, receivables = null, unapplied = null, payables = null;
            PeriodResult? monthResult = null, ytd = null;
            if (ledgerReader)
            {
                cash = (await ledger.CashBankBalancesAsync(monthTo, ct).ConfigureAwait(false)).Where(b => b.Status == AccountingCodes.Active).Sum(b => b.Balance);
                monthResult = await ResultAsync(monthFrom, monthTo, ct).ConfigureAwait(false);
                ytd = await ResultAsync(yearFrom, monthTo, ct).ConfigureAwait(false);
            }
            if (ageingReader)
            {
                var r = await ageing.AgeingAsync("receivables", monthTo, ct).ConfigureAwait(false);
                receivables = r.Totals.Total;
                unapplied = r.Totals.Unapplied;
                receivablesAgeing = r.Totals;
                payables = (await ageing.AgeingAsync("payables", monthTo, ct).ConfigureAwait(false)).Totals.Total;
            }
            figures = new AccountingFigures(cash, receivables, unapplied, payables, monthResult, ytd);
        }
        var trend = ledgerReader ? await TrendAsync(monthFrom, ct).ConfigureAwait(false) : null;
        var budget = Any(PermissionKeys.BudgetView) ? await BudgetUsageAsync(y, yearFrom, monthTo, ct).ConfigureAwait(false) : null;
        var reminders = await RemindersAsync(today, y, m, ct).ConfigureAwait(false);
        return new AccountingDashboardResponse(y, m, monthFrom, monthTo, yearFrom, figures, budget, trend, receivablesAgeing, reminders, clock.GetUtcNow().UtcDateTime);
    }

    // ------------------------------------------------------------ figures

    private async Task<PeriodResult> ResultAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var sums = await db.Set<EntryLine>().AsNoTracking()
            .Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && (l.Account.AccountType == BudgetService.Revenue || l.Account.AccountType == BudgetService.Expense))
            .GroupBy(l => l.Account.AccountType).Select(g => new { g.Key, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .ToListAsync(ct).ConfigureAwait(false);
        var revenue = sums.Where(s => s.Key == BudgetService.Revenue).Sum(s => s.Credit - s.Debit);
        var expenses = sums.Where(s => s.Key == BudgetService.Expense).Sum(s => s.Debit - s.Credit);
        return new PeriodResult(revenue, expenses, revenue - expenses);
    }

    /// <summary>Revenue and expenses of the 12 months ending with the chosen month (oldest first; months without entries are zero).</summary>
    private async Task<IReadOnlyList<MonthAmounts>> TrendAsync(DateOnly monthFrom, CancellationToken ct)
    {
        var from = monthFrom.AddMonths(-11);
        var to = monthFrom.AddMonths(1).AddDays(-1);
        var sums = await db.Set<EntryLine>().AsNoTracking()
            .Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && (l.Account.AccountType == BudgetService.Revenue || l.Account.AccountType == BudgetService.Expense))
            .GroupBy(l => new { l.JournalEntry.EntryDate.Year, l.JournalEntry.EntryDate.Month, l.Account.AccountType })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.AccountType, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .ToListAsync(ct).ConfigureAwait(false);
        return Enumerable.Range(0, 12).Select(i => from.AddMonths(i)).Select(d => new MonthAmounts(AccountingCodes.Month(d),
            sums.Where(s => s.Year == d.Year && s.Month == d.Month && s.AccountType == BudgetService.Revenue).Sum(s => s.Credit - s.Debit),
            sums.Where(s => s.Year == d.Year && s.Month == d.Month && s.AccountType == BudgetService.Expense).Sum(s => s.Debit - s.Credit))).ToList();
    }

    private async Task<BudgetUsage?> BudgetUsageAsync(int year, DateOnly yearFrom, DateOnly to, CancellationToken ct)
    {
        var budget = await budgets.ApprovedAsync(year, ct).ConfigureAwait(false);
        if (budget is null) return null;
        var totals = await db.Set<BudgetLine>().AsNoTracking().Where(l => l.BudgetId == budget.Id)
            .GroupBy(l => l.Account.AccountType).Select(g => new { g.Key, Total = g.Sum(l => l.Total) }).ToListAsync(ct).ConfigureAwait(false);
        var expenseBudget = totals.Where(t => t.Key == BudgetService.Expense).Sum(t => t.Total);
        var revenueBudget = totals.Where(t => t.Key == BudgetService.Revenue).Sum(t => t.Total);
        var actual = await ResultAsync(yearFrom, to, ct).ConfigureAwait(false);
        var days = new DateOnly(year, 12, 31).DayNumber - yearFrom.DayNumber + 1;
        var elapsed = Math.Round((to.DayNumber - yearFrom.DayNumber + 1) * 100m / days, 1);
        static decimal? Pct(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero);
        return new BudgetUsage(support.Token<Budget>(budget.PublicId), budget.Name, expenseBudget, actual.Expenses, Pct(actual.Expenses, expenseBudget),
            revenueBudget, actual.Revenue, Pct(actual.Revenue, revenueBudget), elapsed);
    }

    // ------------------------------------------------------------ reminders

    private async Task<List<AccountingReminder>> RemindersAsync(DateOnly today, int year, int month, CancellationToken ct)
    {
        var list = new List<AccountingReminder>();
        void Add(string key, string severity, int count, decimal? amount, string route)
        {
            if (count > 0) list.Add(new AccountingReminder(key, severity, count, amount, route));
        }

        // Draft journal entries not posted.
        if (Any(PermissionKeys.JeView, PermissionKeys.AcctView, PermissionKeys.AcctPost))
        {
            var drafts = await db.Set<JournalDraft>().AsNoTracking().GroupBy(_ => 1).Select(g => new { N = g.Count(), V = g.Sum(d => d.TotalDebit) })
                .SingleOrDefaultAsync(ct).ConfigureAwait(false);
            Add(AccountingReminderKeys.DraftJournals, "info", drafts?.N ?? 0, drafts?.V, "accounting/journal?view=Draft");
        }

        // Automatic postings waiting (failed, retried by the worker).
        if (Any(PermissionKeys.JeView, PermissionKeys.JeExport))
            Add(AccountingReminderKeys.PendingPostings, "error", await db.Set<PendingPosting>().AsNoTracking().CountAsync(p => p.ResolvedAt == null, ct).ConfigureAwait(false), null,
                "accounting/automatic?tab=pending");

        // Previous months still open: from the month of the first entry to last month, the months not closed.
        if (Any(PermissionKeys.AcctView, PermissionKeys.SettingsManage))
        {
            var first = await db.Set<JournalEntry>().AsNoTracking().MinAsync(e => (DateOnly?)e.EntryDate, ct).ConfigureAwait(false);
            var current = new DateOnly(today.Year, today.Month, 1);
            if (first is DateOnly f && new DateOnly(f.Year, f.Month, 1) < current)
            {
                var months = Enumerable.Range(0, int.MaxValue).Select(i => new DateOnly(f.Year, f.Month, 1).AddMonths(i)).TakeWhile(d => d < current)
                    .Select(AccountingCodes.Month).ToList();
                var closed = await db.Set<Period>().AsNoTracking().Where(p => p.Status == "Closed" && months.Contains(p.PeriodMonth)).CountAsync(ct).ConfigureAwait(false);
                Add(AccountingReminderKeys.OpenMonths, "warning", months.Count - closed, null, "accounting/periods");
            }
        }

        // Overdue customer invoices: transport invoices (invoice rights) and receivable documents (accounting rights).
        var canInvoices = Any(PermissionKeys.InvView);
        var canApAr = Any(PermissionKeys.AcctApar, PermissionKeys.AcctView);
        if (canInvoices || canApAr)
        {
            int n = 0;
            decimal v = 0;
            if (canInvoices)
            {
                var o = await db.Set<Invoice>().AsNoTracking().Where(i => i.Status == InvoiceCodes.Approved && i.RemainingAmount > 0 && i.DueDate < today).GroupBy(_ => 1)
                    .Select(g => new { N = g.Count(), V = g.Sum(i => i.RemainingAmount) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
                (n, v) = (o?.N ?? 0, o?.V ?? 0);
            }
            if (canApAr)
            {
                var o = await db.Set<ApArDocument>().AsNoTracking()
                    .Where(d => d.DocumentKind == ApArCodes.Receivable && d.Status == ApArCodes.Posted && d.RemainingAmount > 0 && d.DueDate < today).GroupBy(_ => 1)
                    .Select(g => new { N = g.Count(), V = g.Sum(d => d.RemainingAmount) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
                (n, v) = (n + (o?.N ?? 0), v + (o?.V ?? 0));
            }
            Add(AccountingReminderKeys.OverdueInvoices, "error", n, v, canInvoices ? "invoices?overdue=true" : "accounting/receivables?view=overdue");
        }

        // Supplier bills due in the next 7 days (not yet overdue, still open).
        if (canApAr)
        {
            var limit = today.AddDays(DueSoonDays);
            var due = await db.Set<ApArDocument>().AsNoTracking()
                .Where(d => d.DocumentKind == ApArCodes.SupplierBill && d.Status == ApArCodes.Posted && d.RemainingAmount > 0 && d.DueDate >= today && d.DueDate <= limit)
                .GroupBy(_ => 1).Select(g => new { N = g.Count(), V = g.Sum(d => d.RemainingAmount) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            Add(AccountingReminderKeys.BillsDueSoon, "warning", due?.N ?? 0, due?.V, "accounting/payables?view=due-soon");
        }

        // Rejected e-invoices (or queued with an error to correct).
        if (canInvoices || canApAr)
        {
            var rejected = 0;
            if (canInvoices)
                rejected += await db.Set<EInvoiceSubmission>().AsNoTracking().CountAsync(s => s.Status == EInvoiceCodes.Rejected || (s.Status == EInvoiceCodes.Queued && s.LastError != null), ct)
                    .ConfigureAwait(false);
            if (canApAr)
                rejected += await db.Set<ApArEInvoiceSubmission>().AsNoTracking().CountAsync(s => s.Status == EInvoiceCodes.Rejected || (s.Status == EInvoiceCodes.Queued && s.LastError != null), ct)
                    .ConfigureAwait(false);
            Add(AccountingReminderKeys.RejectedEInvoices, "error", rejected, null, canInvoices ? "e-invoicing?status=attention" : "accounting/receivables");
        }

        // Budget lines over 100% (expense lines whose actual from January to the month exceeds their budget for the same months).
        if (Any(PermissionKeys.BudgetView))
            Add(AccountingReminderKeys.BudgetOver, "warning", await BudgetOverAsync(year, month, ct).ConfigureAwait(false), null, $"accounting/budget-vs-actual?year={year}");

        // Receipts with an unallocated amount (on account, or partly allocated).
        if (Any(PermissionKeys.RcptManage, PermissionKeys.InvView))
        {
            var u = await db.Set<Receipt>().AsNoTracking().Where(r => r.Status == CashCodes.Active)
                .Select(r => r.Amount - (r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0)).Where(x => x > 0).GroupBy(_ => 1)
                .Select(g => new { N = g.Count(), V = g.Sum(x => x) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            Add(AccountingReminderKeys.UnallocatedReceipts, "info", u?.N ?? 0, u?.V, "receipts?view=unallocated");
        }

        // "Supplier deductions" not mapped while bills with deductions exist or are being entered (a bill refused for it in the last 30 days).
        var canMap = Any(PermissionKeys.SettingsManage);
        if (canMap || Any(PermissionKeys.AcctApar))
        {
            var mapped = await db.Set<PostingKeyMapping>().AsNoTracking().AnyAsync(k => k.PostingKey == PostingKeys.SupplierDeductions, ct).ConfigureAwait(false);
            if (!mapped)
            {
                var bills = await db.Set<ApArDocument>().AsNoTracking().CountAsync(d => d.DocumentKind == ApArCodes.SupplierBill && d.Status == ApArCodes.Posted && d.Deduction > 0, ct)
                    .ConfigureAwait(false);
                var attempted = await db.Set<CompanySetting>().AsNoTracking().Where(s => s.SettingKey == SettingKeys.SupplierDeductionAttempted)
                    .Select(s => s.SettingValue).SingleOrDefaultAsync(ct).ConfigureAwait(false);
                var recent = DateOnly.TryParseExact(attempted, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at)
                    && at >= today.AddDays(-DeductionAttemptDays);
                Add(AccountingReminderKeys.DeductionsUnmapped, "error", Math.Max(bills, recent ? 1 : 0), null, canMap ? "settings/mapping" : "accounting/payables");
            }
        }
        return list;
    }

    /// <summary>
    /// Expense lines of the approved budget whose actual from January to the month exceeds their budget for those months (budget &gt; 0). A line
    /// with a cost centre compares the actuals of that cost centre; a line without one, the actuals of the cost centres without their own line
    /// for that account. A parent account's line takes its sub-accounts' actuals.
    /// </summary>
    internal async Task<int> BudgetOverAsync(int year, int month, CancellationToken ct)
    {
        var budget = await budgets.ApprovedAsync(year, ct).ConfigureAwait(false);
        if (budget is null) return 0;
        var lines = await db.Set<BudgetLine>().AsNoTracking().Where(l => l.BudgetId == budget.Id && l.Account.AccountType == BudgetService.Expense).ToListAsync(ct).ConfigureAwait(false);
        if (lines.Count == 0) return 0;
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        var actuals = await db.Set<EntryLine>().AsNoTracking()
            .Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to && l.Account.AccountType == BudgetService.Expense)
            .GroupBy(l => new { l.AccountId, l.CostCentreId }).Select(g => new { g.Key.AccountId, g.Key.CostCentreId, A = g.Sum(l => l.Debit - l.Credit) })
            .ToListAsync(ct).ConfigureAwait(false);
        var tree = await ledger.TreeAsync(ct).ConfigureAwait(false);
        HashSet<long> Subtree(long id)
        {
            var set = new HashSet<long>();
            var stack = new Stack<long>([id]);
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                if (!set.Add(x) || !tree.TryGetValue(x, out var n)) continue;
                foreach (var c in n.Children) stack.Push(c);
            }
            return set;
        }
        var count = 0;
        foreach (var line in lines)
        {
            var budgetToDate = BudgetMath.Sum(line.Months(), 1, month);
            if (budgetToDate <= 0) continue;
            var accounts = Subtree(line.AccountId);
            var ownCentres = lines.Where(l => l.AccountId == line.AccountId && l.CostCentreId is not null).Select(l => l.CostCentreId).ToHashSet();
            var actual = actuals.Where(a => accounts.Contains(a.AccountId) && (line.CostCentreId is long c ? a.CostCentreId == c : !ownCentres.Contains(a.CostCentreId)))
                .Sum(a => a.A);
            if (actual > budgetToDate) count++;
        }
        return count;
    }
}
