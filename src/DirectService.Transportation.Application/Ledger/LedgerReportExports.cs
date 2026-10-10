using System.Globalization;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Ledger;

namespace DirectService.Transportation.Application.Ledger;

/// <summary>Excel / CSV / PDF exports of the financial reports (FR-121, FR-150) with the filters printed.</summary>
public sealed class LedgerReportExports(LedgerReportService reports, ReportExporter exporter)
{
    private string L(string ar, string en) => exporter.L(ar, en);

    private string Name(AccountRef a) => a.Code + " " + (exporter.En ? a.NameEn ?? a.NameAr : a.NameAr);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private (string, string) Period(DateOnly from, DateOnly to) => (L("الفترة", "Period"), Date(from) + " – " + Date(to));

    private string Note(decimal? openingDifference) => openingDifference is decimal d
        ? L("فرق الأرصدة الافتتاحية لم يُسوَّ بعد: ", "Opening balance difference not cleared yet: ") + d.ToString("#,##0.000", CultureInfo.InvariantCulture)
        : string.Empty;

    public async Task<GeneratedFile> LedgerAsync(LedgerQuery q, string? format, bool statement, CancellationToken ct)
    {
        var r = await reports.LedgerAllAsync(q, ct).ConfigureAwait(false);
        var filters = new List<(string, string)> { (L("الحساب", "Account"), Name(r.Account)), Period(r.From, r.To) };
        if (r.Party is not null) filters.Add((L("الطرف", "Party"), r.Party.Name ?? string.Empty));
        if (r.CostCentre is not null) filters.Add((L("مركز التكلفة", "Cost centre"), r.CostCentre.Name));
        var rows = new List<IReadOnlyList<object?>> { new object?[] { r.From, null, L("رصيد افتتاحي", "Opening balance"), null, null, null, null, r.Opening } };
        rows.AddRange(r.Lines.Items.Select(l => (IReadOnlyList<object?>)[l.Date, l.Entry.Number, l.Description, l.Source?.Number, l.Party?.Name, l.Debit == 0 ? null : l.Debit,
            l.Credit == 0 ? null : l.Credit, l.Balance]));
        var table = new ReportTable(statement ? L("كشف حساب", "Account statement") : L("الأستاذ العام", "General ledger"), filters,
            [new(L("التاريخ", "Date"), ReportValue.Date), new(L("القيد", "Entry")), new(L("البيان", "Description")), new(L("المستند", "Document")), new(L("الطرف", "Party")),
             new(L("مدين", "Debit"), ReportValue.Money), new(L("دائن", "Credit"), ReportValue.Money), new(L("الرصيد", "Balance"), ReportValue.Money)],
            rows, [null, null, L("الإجمالي والرصيد الختامي", "Totals and closing balance"), null, null, r.TotalDebit, r.TotalCredit, r.Closing]);
        return await exporter.ExportAsync(table, format, (statement ? "account-statement-" : "general-ledger-") + r.Account.Code, ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> TrialBalanceAsync(TrialBalanceQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.TrialBalanceAsync(q, ct).ConfigureAwait(false);
        var t = r.Totals;
        var table = new ReportTable(L("ميزان المراجعة", "Trial balance"), [Period(r.From, r.To), (L("المستوى", "Level"), r.Level.ToString(CultureInfo.InvariantCulture))],
            [new(L("الحساب", "Account")), new(L("افتتاحي مدين", "Opening debit"), ReportValue.Money), new(L("افتتاحي دائن", "Opening credit"), ReportValue.Money),
             new(L("حركة مدين", "Movement debit"), ReportValue.Money), new(L("حركة دائن", "Movement credit"), ReportValue.Money),
             new(L("ختامي مدين", "Closing debit"), ReportValue.Money), new(L("ختامي دائن", "Closing credit"), ReportValue.Money)],
            r.Rows.Select(x => (IReadOnlyList<object?>)[Name(x.Account), x.OpeningDebit, x.OpeningCredit, x.MovementDebit, x.MovementCredit, x.ClosingDebit, x.ClosingCredit]).ToList(),
            [L("الإجمالي", "Total"), t.OpeningDebit, t.OpeningCredit, t.MovementDebit, t.MovementCredit, t.ClosingDebit, t.ClosingCredit],
            (r.Balanced ? string.Empty : L("الميزان غير متوازن. ", "The trial balance does not balance. ")) + Note(r.OpeningDifference) is { Length: > 0 } n ? n : null);
        return await exporter.ExportAsync(table, format, "trial-balance", ct).ConfigureAwait(false);
    }

    private IEnumerable<IReadOnlyList<object?>> Lines(IEnumerable<StatementRow> rows, bool compare) => rows.Select(x => compare
        ? (IReadOnlyList<object?>)[new string(' ', (x.Level - 1) * 2) + Name(x.Account), x.Amount, x.CompareAmount]
        : [new string(' ', (x.Level - 1) * 2) + Name(x.Account), x.Amount]);

    public async Task<GeneratedFile> ProfitLossAsync(ProfitLossQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.ProfitLossAsync(q, ct).ConfigureAwait(false);
        var compare = r.CompareFrom is not null;
        var filters = new List<(string, string)> { Period(r.From, r.To) };
        if (r.CostCentre is not null) filters.Add((L("مركز التكلفة", "Cost centre"), r.CostCentre.Name));
        if (compare) filters.Add((L("مقارنة", "Compared with"), Date(r.CompareFrom!.Value) + " – " + Date(r.CompareTo!.Value)));
        var rows = new List<IReadOnlyList<object?>> { ReportExporter.Group(L("الإيرادات", "Revenue")) };
        rows.AddRange(Lines(r.Revenue, compare));
        rows.Add(compare ? [L("مجموع الإيرادات", "Total revenue"), r.TotalRevenue, r.CompareTotalRevenue] : [L("مجموع الإيرادات", "Total revenue"), r.TotalRevenue]);
        rows.Add(ReportExporter.Group(L("المصروفات", "Expenses")));
        rows.AddRange(Lines(r.Expenses, compare));
        rows.Add(compare ? [L("مجموع المصروفات", "Total expenses"), r.TotalExpenses, r.CompareTotalExpenses] : [L("مجموع المصروفات", "Total expenses"), r.TotalExpenses]);
        List<ReportColumn> columns = [new(L("البند", "Item")), new(L("المبلغ", "Amount"), ReportValue.Money)];
        if (compare) columns.Add(new(L("فترة المقارنة", "Comparison"), ReportValue.Money));
        var table = new ReportTable(L("قائمة الأرباح والخسائر", "Profit and loss"), filters, columns, rows,
            compare ? [L("صافي الربح", "Net profit"), r.NetProfit, r.CompareNetProfit] : [L("صافي الربح", "Net profit"), r.NetProfit]);
        return await exporter.ExportAsync(table, format, "profit-and-loss", ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> BalanceSheetAsync(BalanceSheetQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.BalanceSheetAsync(q, ct).ConfigureAwait(false);
        var rows = new List<IReadOnlyList<object?>> { ReportExporter.Group(L("الأصول", "Assets")) };
        rows.AddRange(Lines(r.Assets, false));
        rows.Add([L("مجموع الأصول", "Total assets"), r.TotalAssets]);
        rows.Add(ReportExporter.Group(L("الخصوم", "Liabilities")));
        rows.AddRange(Lines(r.Liabilities, false));
        rows.Add([L("مجموع الخصوم", "Total liabilities"), r.TotalLiabilities]);
        rows.Add(ReportExporter.Group(L("حقوق الملكية", "Equity")));
        rows.AddRange(Lines(r.Equity, false));
        rows.Add([L("نتيجة السنة الحالية", "Current year result"), r.CurrentYearResult]);
        if (r.PriorYearsResult != 0) rows.Add([L("نتائج سنوات سابقة غير مرحّلة", "Results of prior years not transferred"), r.PriorYearsResult]);
        rows.Add([L("مجموع حقوق الملكية", "Total equity"), r.TotalEquity]);
        var table = new ReportTable(L("الميزانية العمومية", "Balance sheet"), [(L("كما في", "As of"), Date(r.AsOf))],
            [new(L("البند", "Item")), new(L("المبلغ", "Amount"), ReportValue.Money)], rows,
            [L("مجموع الخصوم وحقوق الملكية", "Total liabilities and equity"), r.TotalLiabilitiesAndEquity], Note(r.OpeningDifference) is { Length: > 0 } n ? n : null);
        return await exporter.ExportAsync(table, format, "balance-sheet", ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> CostCentresAsync(PeriodQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.CostCentresAsync(q, ct).ConfigureAwait(false);
        var table = new ReportTable(L("تقرير مراكز التكلفة", "Cost-centre report"), [Period(r.From, r.To)],
            [new(L("مركز التكلفة", "Cost centre")), new(L("الإيرادات", "Revenue"), ReportValue.Money), new(L("المصروفات", "Expenses"), ReportValue.Money),
             new(L("الصافي", "Net"), ReportValue.Money)],
            r.Rows.Select(x => (IReadOnlyList<object?>)[x.CostCentre?.Name ?? L("بلا مركز تكلفة", "No cost centre"), x.Revenue, x.Expenses, x.Net]).ToList(),
            [L("الإجمالي", "Total"), r.Totals.Revenue, r.Totals.Expenses, r.Totals.Net]);
        return await exporter.ExportAsync(table, format, "cost-centres", ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> CashBankAsync(CashBankStatementQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.CashBankStatementAsync(q, ct, all: true).ConfigureAwait(false);
        var rows = new List<IReadOnlyList<object?>> { new object?[] { r.From, null, L("رصيد افتتاحي", "Opening balance"), null, null, r.Opening } };
        rows.AddRange(r.Lines.Items.Select(l => (IReadOnlyList<object?>)[l.Date, l.Source?.Number ?? l.Entry.Number, l.Description, l.Debit == 0 ? null : l.Debit,
            l.Credit == 0 ? null : l.Credit, l.Balance]));
        var table = new ReportTable(L("كشف ", "Statement of ") + r.Account.Name, [Period(r.From, r.To), (L("حساب الأستاذ", "Ledger account"), Name(r.LedgerAccount))],
            [new(L("التاريخ", "Date"), ReportValue.Date), new(L("المستند", "Document")), new(L("البيان", "Description")), new(L("مقبوضات", "Receipts"), ReportValue.Money),
             new(L("مدفوعات", "Payments"), ReportValue.Money), new(L("الرصيد", "Balance"), ReportValue.Money)],
            rows, [null, null, L("الإجمالي والرصيد الختامي", "Totals and closing balance"), r.Receipts, r.Payments, r.Closing]);
        return await exporter.ExportAsync(table, format, "cash-bank-statement", ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> ReconciliationAsync(ReconciliationQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.ReconciliationAsync(q, ct).ConfigureAwait(false);
        var table = new ReportTable(L("مطابقة الحسابات الفرعية", "Sub-ledger reconciliation"), [(L("كما في", "As of"), Date(r.AsOf))],
            [new(L("حساب الرقابة", "Control account")), new(L("نوع الطرف", "Party type")), new(L("رصيد الأستاذ", "Ledger balance"), ReportValue.Money),
             new(L("مجموع أرصدة الأطراف", "Party balances"), ReportValue.Money), new(L("الفرق", "Difference"), ReportValue.Money),
             new(L("الأطراف", "Parties"), ReportValue.Number), new(L("سطور غير مطابقة", "Unmatched lines"), ReportValue.Number),
             new(L("رصيد المستندات", "Documents balance"), ReportValue.Money), new(L("فرق المستندات", "Documents difference"), ReportValue.Money)],
            r.Rows.Select(x => (IReadOnlyList<object?>)[Name(x.Account), x.PartyType, x.LedgerBalance, x.PartyBalances, x.Difference, x.Parties, x.Unmatched.Count,
                x.DocumentBalance, x.DocumentDifference]).ToList(),
            null, r.PendingPostings > 0 ? L("ترحيلات معلّقة لم تدخل الأستاذ بعد: ", "Pending postings not in the ledger yet: ") + r.PendingPostings.ToString(CultureInfo.InvariantCulture) : null);
        return await exporter.ExportAsync(table, format, "reconciliation", ct).ConfigureAwait(false);
    }
}
