using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Ledger;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Budgets;

/// <summary>
/// Budget vs Actual (revision 6 part B): the company's approved budget of a year against the posted ledger, for one month, January to the
/// end of a month (period to date) or the whole year, optionally for one cost centre. Rows follow the chart (revenue, then expenses); a parent
/// totals its children (its own budget line, if any, plus theirs). Actuals use every entry that is not soft-deleted, like the other
/// financial reports, in the natural direction of the account. Variance = Actual − Budget; favourable / unfavourable per
/// <see cref="BudgetMath.Status"/>. Excel / PDF through the report exporter.
/// </summary>
public sealed class BudgetReportService(IAppDbContext db, MasterDataSupport support, LedgerReportService ledger, ChartOfAccountsService chart, ReportExporter exporter)
{
    public static readonly IReadOnlyList<string> Modes = ["month", "ytd", "year"];

    /// <summary>The approved budget of a year, or null.</summary>
    public async Task<Budget?> ApprovedAsync(int year, CancellationToken ct) =>
        await db.Set<Budget>().AsNoTracking().SingleOrDefaultAsync(b => b.FiscalYear == year && b.Status == BudgetStatus.Approved, ct).ConfigureAwait(false);

    public async Task<BudgetVsActualReport> ReportAsync(BudgetVsActualQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var year = q.Year ?? today.Year;
        var month = q.Month ?? (year == today.Year ? today.Month : 12);
        var mode = string.IsNullOrWhiteSpace(q.Mode) ? "ytd" : q.Mode.Trim().ToLowerInvariant();
        new Validator()
            .Check(year is >= BudgetService.MinYear and <= BudgetService.MaxYear, "year", ErrorCodes.ValidationRange)
            .Check(month is >= 1 and <= 12, "month", ErrorCodes.ValidationRange)
            .Check(Modes.Contains(mode), "mode", ErrorCodes.ValidationFormat)
            .ThrowIfInvalid();
        var budget = await ApprovedAsync(year, ct).ConfigureAwait(false) ?? throw Fail.Rule(ErrorCodes.BudgetNoneApproved, year);
        long? centre = null;
        Ref? centreRef = null;
        if (!string.IsNullOrWhiteSpace(q.CostCentre))
        {
            var c = await support.FindAsync<CostCentre>(q.CostCentre, ct).ConfigureAwait(false);
            centre = c.Id;
            centreRef = new Ref(support.Token<CostCentre>(c.PublicId), $"{c.Code} – {c.Name}");
        }
        var (first, last) = BudgetMath.Months(mode, month);
        var from = new DateOnly(year, first, 1);
        var to = new DateOnly(year, last, DateTime.DaysInMonth(year, last));

        var lines = await db.Set<BudgetLine>().AsNoTracking().Where(l => l.BudgetId == budget.Id && (centre == null || l.CostCentreId == centre))
            .ToListAsync(ct).ConfigureAwait(false);
        var budgets = new Dictionary<long, decimal>();
        foreach (var l in lines) budgets[l.AccountId] = budgets.GetValueOrDefault(l.AccountId) + BudgetMath.Sum(l.Months(), first, last);
        var actuals = await ActualsAsync(from, to, centre, ct).ConfigureAwait(false);

        var tree = await ledger.TreeAsync(ct).ConfigureAwait(false);
        var rows = new List<BudgetVsActualRow>();
        (decimal B, decimal A) Walk(long id, string? parentToken, List<BudgetVsActualRow> into)
        {
            var n = tree[id];
            var own = new List<BudgetVsActualRow>();
            var token = chart.Ref(n.Account).Id;
            decimal b = budgets.GetValueOrDefault(id), a = actuals.GetValueOrDefault(id);
            foreach (var child in n.Children.OrderBy(c => tree[c].Account.Code, StringComparer.Ordinal))
            {
                var (cb, ca) = Walk(child, token, own);
                b += cb;
                a += ca;
            }
            if (b != 0 || a != 0 || budgets.ContainsKey(id))
            {
                var type = n.Account.AccountType;
                into.Add(new BudgetVsActualRow(chart.Ref(n.Account), type, n.Depth, own.Count > 0, parentToken, b, a, BudgetMath.Variance(b, a),
                    BudgetMath.VariancePercent(b, a), BudgetMath.Status(type, b, a)));
                into.AddRange(own);
            }
            return (b, a);
        }
        decimal rb = 0, ra = 0, eb = 0, ea = 0;
        foreach (var type in new[] { BudgetService.Revenue, BudgetService.Expense })
        {
            foreach (var root in tree.Values.Where(n => n.Account.AccountType == type && (n.Account.ParentId is not long p || !tree.ContainsKey(p) || tree[p].Account.AccountType != type))
                         .OrderBy(n => n.Account.Code, StringComparer.Ordinal))
            {
                var (b, a) = Walk(root.Account.Id, null, rows);
                if (type == BudgetService.Revenue) { rb += b; ra += a; }
                else { eb += b; ea += a; }
            }
        }
        return new BudgetVsActualReport(new Ref(support.Token<Budget>(budget.PublicId), budget.Name), year, month, mode, from, to, centreRef, rows,
            Total(BudgetService.Revenue, rb, ra), Total(BudgetService.Expense, eb, ea), Total(BudgetService.Revenue, rb - eb, ra - ea));
    }

    private static BudgetVsActualTotal Total(string type, decimal budget, decimal actual) =>
        new(budget, actual, BudgetMath.Variance(budget, actual), BudgetMath.VariancePercent(budget, actual), BudgetMath.Status(type, budget, actual));

    /// <summary>Natural amounts per revenue / expense account posted from <paramref name="from"/> to <paramref name="to"/> (optionally one cost centre).</summary>
    internal async Task<Dictionary<long, decimal>> ActualsAsync(DateOnly from, DateOnly to, long? centre, CancellationToken ct)
    {
        var q = db.Set<EntryLine>().AsNoTracking()
            .Where(l => l.JournalEntry.EntryDate >= from && l.JournalEntry.EntryDate <= to
                && (l.Account.AccountType == BudgetService.Revenue || l.Account.AccountType == BudgetService.Expense));
        if (centre is long c) q = q.Where(l => l.CostCentreId == c);
        var sums = await q.GroupBy(l => new { l.AccountId, l.Account.AccountType })
            .Select(g => new { g.Key.AccountId, g.Key.AccountType, Debit = g.Sum(l => l.Debit), Credit = g.Sum(l => l.Credit) })
            .ToListAsync(ct).ConfigureAwait(false);
        return sums.ToDictionary(s => s.AccountId, s => BudgetMath.Natural(s.AccountType, s.Debit, s.Credit));
    }

    // ------------------------------------------------------------ export

    public async Task<GeneratedFile> ExportAsync(BudgetVsActualQuery q, string? format, CancellationToken ct)
    {
        var r = await ReportAsync(q, ct).ConfigureAwait(false);
        string L(string ar, string en) => exporter.L(ar, en);
        string Name(AccountRef a) => a.Code + " " + (exporter.En ? a.NameEn ?? a.NameAr : a.NameAr);
        string Status(string s) => s switch
        {
            BudgetMath.Favourable => L("ملائم", "Favourable"),
            BudgetMath.Unfavourable => L("غير ملائم", "Unfavourable"),
            _ => L("مطابق", "On budget"),
        };
        decimal? Pct(decimal? p) => p;
        IReadOnlyList<object?> Line(string label, BudgetVsActualTotal t) => [label, t.Budget, t.Actual, t.Variance, Pct(t.VariancePercent), Status(t.Status)];
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var (type, title) in new[] { (BudgetService.Revenue, L("الإيرادات", "Revenue")), (BudgetService.Expense, L("المصروفات", "Expenses")) })
        {
            rows.Add(ReportExporter.Group(title));
            rows.AddRange(r.Rows.Where(x => x.AccountType == type).Select(x => (IReadOnlyList<object?>)
                [new string(' ', (x.Level - 1) * 2) + Name(x.Account), x.Budget, x.Actual, x.Variance, Pct(x.VariancePercent), Status(x.Status)]));
            rows.Add(Line(type == BudgetService.Revenue ? L("إجمالي الإيرادات", "Total revenue") : L("إجمالي المصروفات", "Total expenses"),
                type == BudgetService.Revenue ? r.Revenue : r.Expenses));
        }
        var mode = r.Mode switch { "month" => L("الشهر", "Month"), "year" => L("السنة كاملة", "Whole year"), _ => L("من بداية السنة", "Year to date") };
        var filters = new List<(string, string)>
        {
            (L("الموازنة", "Budget"), r.Budget.Name),
            (L("الفترة", "Period"), $"{D(r.From)} – {D(r.To)} ({mode})"),
        };
        if (r.CostCentre is not null) filters.Add((L("مركز التكلفة", "Cost centre"), r.CostCentre.Name));
        var table = new ReportTable(L("الموازنة مقابل الفعلي", "Budget vs Actual"), filters,
            [new(L("الحساب", "Account")), new(L("الموازنة", "Budget"), ReportValue.Money), new(L("الفعلي", "Actual"), ReportValue.Money),
             new(L("الانحراف", "Variance"), ReportValue.Money), new(L("الانحراف %", "Variance %"), ReportValue.Number), new(L("التقييم", "Assessment"))],
            rows, Line(L("صافي النتيجة", "Net result"), r.Net),
            L("الانحراف = الفعلي − الموازنة. الإيرادات فوق الموازنة والمصروفات دونها ملائمة.", "Variance = Actual − Budget. Revenue above budget and expenses below budget are favourable."));
        return await exporter.ExportAsync(table, format, "budget-vs-actual", ct).ConfigureAwait(false);
    }

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
