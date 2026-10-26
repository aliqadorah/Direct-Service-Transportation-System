using DirectService.Transportation.Application.Accounting;

namespace DirectService.Transportation.Application.Budgets;

/// <summary>The budget calculations (revision 6 part B): natural amounts, variance, variance %, favourable / unfavourable, period months, line conflicts.</summary>
public static class BudgetMath
{
    public const string Favourable = "favourable";
    public const string Unfavourable = "unfavourable";
    public const string OnBudget = "onBudget";

    /// <summary>
    /// The amount of an account in its natural direction: revenue, liabilities and equity = credit − debit; expenses and assets = debit −
    /// credit (balance-sheet accounts may be budgeted since DT-265).
    /// </summary>
    public static decimal Natural(string accountType, decimal debit, decimal credit) =>
        accountType is BudgetService.Revenue or "Liability" or "Equity" ? credit - debit : debit - credit;

    /// <summary>
    /// A yearly amount spread evenly over the twelve months (DT-265): each month the amount ÷ 12 rounded half away from zero to the currency
    /// decimals; December takes the rounding difference, so the months add up to the amount exactly.
    /// </summary>
    public static decimal[] Distribute(decimal yearly, int decimals)
    {
        var month = Math.Round(yearly / 12m, decimals, MidpointRounding.AwayFromZero);
        var months = Enumerable.Repeat(month, 12).ToArray();
        months[11] = yearly - month * 11;
        return months;
    }

    /// <summary>A budget amount: the currency's decimals, any sign, at most <see cref="Money.MaxAmount"/> either way.</summary>
    public static bool IsValidAmount(decimal value, int decimals) => Money.IsValidAmount(Math.Abs(value), decimals);

    /// <summary>An amount increased by a percentage (negative = decrease), rounded half away from zero to the currency decimals.</summary>
    public static decimal Increase(decimal amount, decimal percent, int decimals) =>
        Math.Round(amount * (1 + percent / 100m), decimals, MidpointRounding.AwayFromZero);

    /// <summary>Variance = actual − budget.</summary>
    public static decimal Variance(decimal budget, decimal actual) => actual - budget;

    /// <summary>Variance ÷ |budget| × 100, one decimal; none when the budget is zero.</summary>
    public static decimal? VariancePercent(decimal budget, decimal actual) =>
        budget == 0 ? null : Math.Round((actual - budget) / Math.Abs(budget) * 100m, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Revenue (and the net result) above budget is favourable, below unfavourable; an expense above budget is unfavourable, below favourable;
    /// equal = on budget.
    /// </summary>
    public static string Status(string accountType, decimal budget, decimal actual)
    {
        var v = actual - budget;
        if (v == 0) return OnBudget;
        var moreIsGood = accountType != BudgetService.Expense;
        return (v > 0) == moreIsGood ? Favourable : Unfavourable;
    }

    /// <summary>The months (1–12) a report mode covers: <c>month</c> = that month, <c>ytd</c> = January to it, <c>year</c> = all twelve.</summary>
    public static (int First, int Last) Months(string mode, int month) => mode switch
    {
        "month" => (month, month),
        "year" => (1, 12),
        _ => (1, month),
    };

    /// <summary>Σ of the monthly amounts from <paramref name="first"/> to <paramref name="last"/> (1-based, inclusive).</summary>
    public static decimal Sum(IReadOnlyList<decimal> months, int first, int last)
    {
        ArgumentNullException.ThrowIfNull(months);
        decimal total = 0;
        for (var m = first; m <= last; m++) total += months[m - 1];
        return total;
    }

    /// <summary>
    /// The first line that conflicts with an earlier one: the same account and cost centre twice (<c>AncestorId</c> null), or an account
    /// whose ancestor (or descendant) also has a line for the same cost centre (<c>AncestorId</c> = the other account). Null when none.
    /// </summary>
    public static (int Index, long? AncestorId)? FindConflict(IReadOnlyList<(long AccountId, long? CostCentreId)> lines, Func<long, long?> parentOf)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(parentOf);
        var seen = new Dictionary<(long, long?), int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!seen.TryAdd((lines[i].AccountId, lines[i].CostCentreId), i)) return (i, null);
        }
        for (var i = 0; i < lines.Count; i++)
        {
            var guard = 0;
            for (var p = parentOf(lines[i].AccountId); p is long pid && guard++ < 64; p = parentOf(pid))
            {
                if (seen.ContainsKey((pid, lines[i].CostCentreId))) return (i, pid);
            }
        }
        return null;
    }
}
