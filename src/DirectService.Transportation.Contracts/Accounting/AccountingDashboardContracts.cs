using DirectService.Transportation.Contracts.ApAr;

namespace DirectService.Transportation.Contracts.Accounting;

// Revision 6 part B (owner change request 15 Oct 2026): the accounting dashboard (لوحة المحاسبة). Every part is present only when the user
// holds a permission that opens it (null / left out otherwise).

/// <summary>
/// The accounting dashboard of a month: key figures (as of the end of the month, or today for the current month), budget usage, revenue vs
/// expenses of the last 12 months, receivables ageing and the reminders.
/// </summary>
public sealed record AccountingDashboardResponse(int Year, int Month, DateOnly MonthFrom, DateOnly MonthTo, DateOnly YearFrom, AccountingFigures? Figures,
    BudgetUsage? Budget, IReadOnlyList<MonthAmounts>? Trend, AgeingBuckets? ReceivablesAgeing, IReadOnlyList<AccountingReminder> Reminders, DateTime GeneratedAt);

/// <summary>Key figures; a figure the user may not see is null.</summary>
public sealed record AccountingFigures(decimal? CashAndBank, decimal? Receivables, decimal? ReceivablesUnapplied, Payables? Payables, PeriodResult? Month, PeriodResult? YearToDate);

/// <summary>
/// What is owed (owner decision 18 Oct 2026, DT-200): to suppliers (open supplier bills, as the payables ageing), to drivers (net due of the
/// driver accounts, RPT-19) and to partners (balance due of the partner accounts, RPT-20); a line the user may not open is null; the total
/// is the sum of the lines shown.
/// </summary>
public sealed record Payables(decimal? Suppliers, decimal? Drivers, decimal? Partners, decimal Total);

/// <summary>Revenue, expenses and net profit (revenue − expenses) of a period, from the posted ledger.</summary>
public sealed record PeriodResult(decimal Revenue, decimal Expenses, decimal NetProfit);

/// <summary>
/// The approved budget of the year: expenses used (actual year to date ÷ the year's expense budget), revenue achieved (actual ÷ the year's
/// revenue budget), and how much of the year has passed (for comparison).
/// </summary>
public sealed record BudgetUsage(string BudgetId, string BudgetName, decimal ExpenseBudget, decimal ExpenseActual, decimal? ExpenseUsedPercent,
    decimal RevenueBudget, decimal RevenueActual, decimal? RevenueAchievedPercent, decimal YearElapsedPercent);

/// <summary>Revenue and expenses of one month ("yyyy-MM").</summary>
public sealed record MonthAmounts(string Month, decimal Revenue, decimal Expenses);

/// <summary>
/// A reminder computed from the data (never entered by hand): its key (the screen words it), severity (error / warning / info), how many,
/// an amount where it means something, and the screen it opens (company-relative route with filters).
/// </summary>
public sealed record AccountingReminder(string Key, string Severity, int Count, decimal? Amount, string Route, IReadOnlyList<string>? Items = null);

public static class AccountingReminderKeys
{
    public const string DraftJournals = "draft-journals";
    public const string PendingPostings = "pending-postings";
    public const string OpenMonths = "open-months";
    public const string OverdueInvoices = "overdue-invoices";
    public const string BillsDueSoon = "bills-due-soon";
    public const string RejectedEInvoices = "rejected-einvoices";
    public const string BudgetOver = "budget-over";
    public const string UnallocatedReceipts = "unallocated-receipts";
    public const string DeductionsUnmapped = "deductions-unmapped";
    /// <summary>
    /// DT-228: driver balances carried to the next settlement after a repayment with the settlement; each item names the carried advance,
    /// the driver and "Repaid by driver: amount (method, reference)". Shown to driver-account readers (not in <see cref="All"/>, which lists
    /// the accounting reminders; covered by the cash tests).
    /// </summary>
    public const string CarriedAdvances = "carried-advances";

    public static readonly IReadOnlyList<string> All =
        [DraftJournals, PendingPostings, OpenMonths, OverdueInvoices, BillsDueSoon, RejectedEInvoices, BudgetOver, UnallocatedReceipts, DeductionsUnmapped];
}
