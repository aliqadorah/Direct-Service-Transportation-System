using System.Globalization;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Ledger;

namespace DirectService.Transportation.Application.Ledger;

/// <summary>Excel / CSV / PDF exports of the financial reports (FR-121, FR-150) with the filters printed.</summary>
public sealed class LedgerReportExports(LedgerReportService reports, IReportExporter exporter)
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
        var rows = new List<IReadOnlyList<object?>> { new object?[] { r.From, null, L("رصيد افتتاحي", "Opening balance"), null, null, null, null, null, r.Opening } };
        rows.AddRange(r.Lines.Items.Select(l => (IReadOnlyList<object?>)[l.Date, l.Entry.Number, l.Description, l.Reference, l.Source?.Number, l.Party?.Name, l.Debit == 0 ? null : l.Debit,
            l.Credit == 0 ? null : l.Credit, l.Balance]));
        var table = new ReportTable(statement ? L("كشف حساب", "Account statement") : L("الأستاذ العام", "General ledger"), filters,
            [new(L("التاريخ", "Date"), ReportValue.Date), new(L("القيد", "Entry")), new(L("البيان", "Description")), new(L("المرجع", "Reference")), new(L("المستند", "Document")),
             new(L("الطرف", "Party")), new(L("مدين", "Debit"), ReportValue.Money), new(L("دائن", "Credit"), ReportValue.Money), new(L("الرصيد", "Balance"), ReportValue.Money)],
            rows, [null, null, L("الإجمالي والرصيد الختامي", "Totals and closing balance"), null, null, null, r.TotalDebit, r.TotalCredit, r.Closing]);
        return await exporter.ExportAsync(table, format, (statement ? "account-statement-" : "general-ledger-") + r.Account.Code, ct).ConfigureAwait(false);
    }

    /// <summary>Account statement of several accounts (revision 6): per account a heading, its opening, its lines and its subtotal; the grand total last.</summary>
    public async Task<GeneratedFile> StatementsAsync(AccountStatementsQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.StatementsAsync(q, ct).ConfigureAwait(false);
        var filters = new List<(string, string)> { Period(r.From, r.To), (L("الحسابات", "Accounts"), r.Sections.Count.ToString(CultureInfo.InvariantCulture)) };
        if (r.Party is not null) filters.Add((L("الطرف", "Party"), r.Party.Name ?? string.Empty));
        if (r.CostCentre is not null) filters.Add((L("مركز التكلفة", "Cost centre"), r.CostCentre.Name));
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var s in r.Sections)
        {
            rows.Add(new object?[] { null, null, Name(s.Account), null, null, null, null, null, null });
            rows.Add(new object?[] { r.From, null, L("رصيد افتتاحي", "Opening balance"), null, null, null, null, null, s.Opening });
            rows.AddRange(s.Lines.Select(l => (IReadOnlyList<object?>)[l.Date, l.Entry.Number, l.Description, l.Reference, l.Source?.Number, l.Party?.Name,
                l.Debit == 0 ? null : l.Debit, l.Credit == 0 ? null : l.Credit, l.Balance]));
            rows.Add(new object?[] { null, null, L("المجموع الفرعي ", "Subtotal ") + s.Account.Code, null, null, null, s.TotalDebit, s.TotalCredit, s.Closing });
        }
        var table = new ReportTable(L("كشف حساب", "Account statement"), filters,
            [new(L("التاريخ", "Date"), ReportValue.Date), new(L("القيد", "Entry")), new(L("البيان", "Description")), new(L("المرجع", "Reference")), new(L("المستند", "Document")),
             new(L("الطرف", "Party")), new(L("مدين", "Debit"), ReportValue.Money), new(L("دائن", "Credit"), ReportValue.Money), new(L("الرصيد", "Balance"), ReportValue.Money)],
            rows, [null, null, L("الإجمالي العام", "Grand total"), null, null, null, r.TotalDebit, r.TotalCredit, r.Closing]);
        return await exporter.ExportAsync(table, format, "account-statement", ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> TrialBalanceAsync(TrialBalanceQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.TrialBalanceAsync(q, ct).ConfigureAwait(false);
        var t = r.Totals;
        var table = new ReportTable(L("ميزان المراجعة", "Trial balance"), [Period(r.From, r.To), (L("المستوى", "Level"), r.Level.ToString(CultureInfo.InvariantCulture))],
            [new(L("الحساب", "Account")), new(L("افتتاحي مدين", "Opening debit"), ReportValue.Money), new(L("افتتاحي دائن", "Opening credit"), ReportValue.Money),
             new(L("حركة مدين", "Movement debit"), ReportValue.Money), new(L("حركة دائن", "Movement credit"), ReportValue.Money),
             new(L("ختامي مدين", "Closing debit"), ReportValue.Money), new(L("ختامي دائن", "Closing credit"), ReportValue.Money)],
            // The tree (revision 6) keeps its indentation in Excel and PDF: two spaces per level below the first.
            r.Rows.Select(x => (IReadOnlyList<object?>)[(q.Tree == true ? new string(' ', (x.Level - 1) * 2) : string.Empty) + Name(x.Account), x.OpeningDebit, x.OpeningCredit,
                x.MovementDebit, x.MovementCredit, x.ClosingDebit, x.ClosingCredit]).ToList(),
            [L("الإجمالي", "Total"), t.OpeningDebit, t.OpeningCredit, t.MovementDebit, t.MovementCredit, t.ClosingDebit, t.ClosingCredit],
            (r.Balanced ? string.Empty : L("الميزان غير متوازن. ", "The trial balance does not balance. ")) + Note(r.OpeningDifference) is { Length: > 0 } n ? n : null);
        return await exporter.ExportAsync(table, format, "trial-balance", ct).ConfigureAwait(false);
    }

    private IEnumerable<IReadOnlyList<object?>> Lines(IEnumerable<StatementRow> rows, bool compare) => rows.Select(x => compare
        ? (IReadOnlyList<object?>)[new string(' ', (x.Level - 1) * 2) + Name(x.Account), x.Amount, x.CompareAmount]
        : [new string(' ', (x.Level - 1) * 2) + Name(x.Account), x.Amount]);

    /// <summary>Title of a profit and loss section (DT-264).</summary>
    public string SectionTitle(string group) => group switch
    {
        ProfitLossGroups.Sales => L("المبيعات والإيرادات", "Sales and revenue"),
        ProfitLossGroups.CostOfSales => L("تكلفة المبيعات", "Cost of sales"),
        ProfitLossGroups.Operating => L("المصاريف التشغيلية", "Operating expenses"),
        ProfitLossGroups.OtherIncome => L("إيرادات أخرى", "Other income"),
        _ => L("مصروفات أخرى", "Other expenses"),
    };

    /// <summary>
    /// The profit and loss as on the screen (DT-266): the sections with their accounts indented by level (a group with the total of its
    /// sub-accounts, followed by its sub-accounts), each section's total, gross profit after the cost of sales and net profit last; the same
    /// columns as the screen ("Columns by" and the total; the comparison period with one amount). A PDF with more than six amount columns is
    /// printed landscape.
    /// </summary>
    public async Task<GeneratedFile> ProfitLossAsync(ProfitLossQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.ProfitLossAsync(q, ct).ConfigureAwait(false);
        var compare = r.CompareFrom is not null;
        var single = r.ColumnsBy == "none";
        var filters = new List<(string, string)> { Period(r.From, r.To) };
        if (r.CostCentre is not null) filters.Add((L("مركز التكلفة", "Cost centre"), r.CostCentre.Name));
        if (compare) filters.Add((L("مقارنة", "Compared with"), Date(r.CompareFrom!.Value) + " – " + Date(r.CompareTo!.Value)));
        if (!single)
        {
            filters.Add((L("الأعمدة حسب", "Columns by"), r.ColumnsBy switch
            {
                "costCentre" => L("مركز التكلفة", "Cost centre"),
                "month" => L("الشهر", "Month"),
                "quarter" => L("الربع", "Quarter"),
                _ => L("السنة", "Year"),
            }));
        }
        IReadOnlyList<object?> Values(string label, IReadOnlyList<decimal> amounts, decimal total, decimal? cmp)
        {
            var v = new List<object?> { label };
            if (single) v.Add(total);
            else
            {
                v.AddRange(amounts.Cast<object?>());
                v.Add(total);
            }
            if (compare) v.Add(cmp);
            return v;
        }
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var s in r.Sections ?? [])
        {
            var optional = s.Group is ProfitLossGroups.OtherIncome or ProfitLossGroups.OtherExpense;
            if (s.Rows.Count > 0 || !optional)
            {
                rows.Add(IReportExporter.Group(SectionTitle(s.Group)));
                rows.AddRange(s.Rows.Select(x => Values(new string(' ', (x.Level - 1) * 2) + Name(x.Account), x.Amounts, x.Total, x.CompareAmount)));
                rows.Add(Values(L("مجموع ", "Total ") + SectionTitle(s.Group), s.Total.Amounts, s.Total.Total, s.Total.Compare));
            }
            if (s.Group == ProfitLossGroups.CostOfSales && r.GrossProfit is { } g) rows.Add(Values(L("مجمل الربح", "Gross profit"), g.Amounts, g.Total, g.Compare));
        }
        List<ReportColumn> columns = [new(L("البند", "Item"))];
        if (single) columns.Add(new(L("المبلغ", "Amount"), ReportValue.Money));
        else
        {
            columns.AddRange((r.Columns ?? []).Select(c => new ReportColumn(c.Name ?? c.Key, ReportValue.Money)));
            columns.Add(new(L("الإجمالي", "Total"), ReportValue.Money));
        }
        if (compare) columns.Add(new(L("فترة المقارنة", "Comparison"), ReportValue.Money));
        var net = r.Net ?? new ProfitLossFigure([r.NetProfit], r.NetProfit, r.CompareNetProfit);
        var table = new ReportTable(L("قائمة الأرباح والخسائر", "Profit and loss"), filters, columns, rows,
            Values(net.Total >= 0 ? L("صافي الربح", "Net profit") : L("صافي الخسارة", "Net loss"), net.Amounts, net.Total, net.Compare))
        {
            Landscape = columns.Count > 7,
        };
        return await exporter.ExportAsync(table, format, "profit-and-loss", ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> BalanceSheetAsync(BalanceSheetQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.BalanceSheetAsync(q, ct).ConfigureAwait(false);
        var rows = new List<IReadOnlyList<object?>> { IReportExporter.Group(L("الأصول", "Assets")) };
        rows.AddRange(Lines(r.Assets, false));
        rows.Add([L("مجموع الأصول", "Total assets"), r.TotalAssets]);
        rows.Add(IReportExporter.Group(L("الخصوم", "Liabilities")));
        rows.AddRange(Lines(r.Liabilities, false));
        rows.Add([L("مجموع الخصوم", "Total liabilities"), r.TotalLiabilities]);
        rows.Add(IReportExporter.Group(L("حقوق الملكية", "Equity")));
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

    /// <summary>
    /// RPT-23 export (DT-262): the same figures as the screen for every control account, the documents difference split into manual entries,
    /// the opening entry and any unexplained rest, the entries behind it (one row each, under their account) and the screen's verdict as the note.
    /// </summary>
    public async Task<GeneratedFile> ReconciliationAsync(ReconciliationQuery q, string? format, CancellationToken ct)
    {
        var r = await reports.ReconciliationAsync(q, ct).ConfigureAwait(false);
        var rows = new List<IReadOnlyList<object?>>();
        foreach (var x in r.Rows)
        {
            rows.Add([Name(x.Account), x.PartyType, x.LedgerBalance, x.PartyBalances, x.Difference, x.Parties, x.Unmatched.Count,
                x.DocumentBalance, x.DocumentDifference, x.ManualAmount, x.OpeningAmount, x.Unexplained]);
            foreach (var e in x.GapEntries ?? [])
                rows.Add([$"    ↳ {e.Entry.Number} · {Date(e.Date)} · {e.Description}", e.Kind == AccountingCodes.SourceOpening ? L("القيد الافتتاحي", "Opening entry") : L("قيد يدوي", "Manual entry"),
                    null, null, null, null, null, null, e.Amount, null, null, null]);
        }
        var verdict = r.Level switch
        {
            ReconciliationLevels.Differences => L("توجد فروق بين حسابات الرقابة وأرصدة الأطراف.", "Control accounts differ from the party balances."),
            ReconciliationLevels.Documents => L("حسابات الرقابة مطابقة لأرصدة الأطراف. بعض المبالغ مصدرها قيود يومية يدوية أو القيد الافتتاحي وليس المستندات.",
                "Control accounts match the party balances. Some amounts come from manual journal entries or the opening entry, not from documents."),
            _ => L("الحسابات المساعدة مطابقة لحسابات الرقابة.", "The sub-ledgers match the control accounts."),
        };
        var note = r.PendingPostings > 0
            ? verdict + " " + L("ترحيلات معلّقة لم تدخل الأستاذ بعد: ", "Pending postings not in the ledger yet: ") + r.PendingPostings.ToString(CultureInfo.InvariantCulture)
            : verdict;
        // Excel / CSV have no note band: the verdict is their last row, so every format says what the screen says.
        if (!IReportExporter.IsPrintout(format)) rows.Add([note, null, null, null, null, null, null, null, null, null, null, null]);
        var table = new ReportTable(L("مطابقة الحسابات الفرعية", "Sub-ledger reconciliation"), [(L("كما في", "As of"), Date(r.AsOf))],
            [new(L("حساب الرقابة", "Control account")), new(L("نوع الطرف", "Party type")), new(L("رصيد الأستاذ", "Ledger balance"), ReportValue.Money),
             new(L("مجموع أرصدة الأطراف", "Party balances"), ReportValue.Money), new(L("الفرق", "Difference"), ReportValue.Money),
             new(L("الأطراف", "Parties"), ReportValue.Number), new(L("سطور غير مطابقة", "Unmatched lines"), ReportValue.Number),
             new(L("الرصيد من المستندات", "Balance from documents"), ReportValue.Money), new(L("فرق المستندات", "Documents difference"), ReportValue.Money),
             new(L("منه قيود يدوية", "Of which manual entries"), ReportValue.Money), new(L("منه القيد الافتتاحي", "Of which opening entry"), ReportValue.Money),
             new(L("غير مفسَّر", "Unexplained"), ReportValue.Money)],
            rows, null, note);
        return await exporter.ExportAsync(table, format, "reconciliation", ct).ConfigureAwait(false);
    }
}
