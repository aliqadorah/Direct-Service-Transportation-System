
namespace DirectService.Transportation.Contracts.Accounting;

// Revision 6 part B (owner change request 15 Oct 2026): budgets (الموازنة) and the Budget vs Actual report.

/// <summary>Budget statuses.</summary>
public static class BudgetStatus
{
    public const string Draft = "Draft";
    public const string Approved = "Approved";
}

/// <summary>A budget in the list: year, name, status, yearly total of its lines (revenue and expense separately).</summary>
public sealed record BudgetSummary(string Id, int FiscalYear, string Name, string Status, int Lines, decimal RevenueTotal, decimal ExpenseTotal,
    DateTime? ApprovedAt, string? ApprovedByName, DateTime UpdatedAt, string RowVersion);

/// <summary>A budget with its lines; amounts per month (January first) in the natural direction of the account.</summary>
public sealed record BudgetResponse(string Id, int FiscalYear, string Name, string Status, DateTime? ApprovedAt, string? ApprovedByName,
    IReadOnlyList<BudgetLineResponse> Lines, decimal RevenueTotal, decimal ExpenseTotal, string RowVersion);

/// <summary>A budget line: account (with its type and whether it is a parent), optional cost centre, twelve months and the yearly total.</summary>
public sealed record BudgetLineResponse(AccountRef Account, string AccountType, bool IsParent, Ref? CostCentre, IReadOnlyList<decimal> Months, decimal Total);

/// <summary>An account a budget line may use: active revenue or expense account, posting or parent, with its tree level (1 = top).</summary>
public sealed record BudgetAccountOption(AccountRef Account, string AccountType, bool IsParent, int Level);

/// <summary>
/// An account of the budget screen (DT-265): every account in chart order with its parent and tree level (1 = top); a group (sub-accounts, or
/// not postable) is a heading with subtotals, the others take amounts.
/// </summary>
public sealed record BudgetGridAccount(AccountRef Account, string AccountType, string? ParentId, int Level, bool IsGroup, bool IsActive);

/// <summary>
/// Creates a budget: year and name; empty, or copied from the previous year's actuals (<paramref name="CopyActuals"/>) with an optional
/// percentage increase (may be negative) and optionally one line per cost centre.
/// </summary>
public sealed record CreateBudgetRequest(int FiscalYear, string? Name, bool? CopyActuals = null, decimal? IncreasePercent = null, bool? ByCostCentre = null);

/// <summary>Saves a draft budget: name and every line (lines not sent are removed). Row version required.</summary>
public sealed record BudgetRequest(string? Name, IReadOnlyList<BudgetLineRequest>? Lines, string? RowVersion);

/// <summary>A line to save: account, optional cost centre, twelve monthly amounts (January first).</summary>
public sealed record BudgetLineRequest(string? AccountId, string? CostCentreId, IReadOnlyList<decimal>? Months);

/// <summary>Budget vs Actual: <c>month</c> (that month only), <c>ytd</c> (January to the end of that month) or <c>year</c> (the whole year).</summary>
public sealed record BudgetVsActualQuery(int? Year = null, int? Month = null, string? Mode = null, string? CostCentre = null);

/// <summary>
/// One account of the Budget vs Actual tree (parents total their children). Variance = Actual − Budget; Variance % = Variance ÷ |Budget| × 100
/// (none when the budget is zero). Status: <c>favourable</c> (revenue above budget, expense below), <c>unfavourable</c> (revenue below,
/// expense above) or <c>onBudget</c>.
/// </summary>
public sealed record BudgetVsActualRow(AccountRef Account, string AccountType, int Level, bool IsGroup, string? ParentId, decimal Budget, decimal Actual,
    decimal Variance, decimal? VariancePercent, string Status);

/// <summary>A total line (revenue, expenses, net result = revenue − expenses) with its variance.</summary>
public sealed record BudgetVsActualTotal(decimal Budget, decimal Actual, decimal Variance, decimal? VariancePercent, string Status);

public sealed record BudgetVsActualReport(Ref Budget, int Year, int Month, string Mode, DateOnly From, DateOnly To, Ref? CostCentre,
    IReadOnlyList<BudgetVsActualRow> Rows, BudgetVsActualTotal Revenue, BudgetVsActualTotal Expenses, BudgetVsActualTotal Net);
