using DirectService.Transportation.Application.Budgets;

namespace DirectService.Transportation.Application.Tests;

/// <summary>Revision 6 part B: budget calculations — natural direction, variance and its sign, variance %, periods, increase, line conflicts.</summary>
public sealed class BudgetMathTests
{
    [Theory]
    [InlineData("Revenue", 1000, 1200, "favourable")]     // revenue above budget
    [InlineData("Revenue", 1000, 800, "unfavourable")]    // revenue below budget
    [InlineData("Expense", 400, 350, "favourable")]       // expense below budget
    [InlineData("Expense", 10, 30, "unfavourable")]       // expense above budget
    [InlineData("Expense", 0, 5, "unfavourable")]         // unbudgeted expense
    [InlineData("Revenue", 50, 50, "onBudget")]
    public void Favourable_or_unfavourable_by_account_type(string type, int budget, int actual, string expected) =>
        Assert.Equal(expected, BudgetMath.Status(type, budget, actual));

    [Fact]
    public void Variance_is_actual_minus_budget_and_percent_is_of_the_absolute_budget()
    {
        Assert.Equal(200m, BudgetMath.Variance(1000, 1200));
        Assert.Equal(-50m, BudgetMath.Variance(400, 350));
        Assert.Equal(20.0m, BudgetMath.VariancePercent(1000, 1200));
        Assert.Equal(-12.5m, BudgetMath.VariancePercent(400, 350));
        Assert.Equal(-72.7m, BudgetMath.VariancePercent(110, 30));
        Assert.Equal(50.0m, BudgetMath.VariancePercent(-20, -10));   // a negative budget line: % of its size
        Assert.Null(BudgetMath.VariancePercent(0, 300));
    }

    [Fact]
    public void Natural_amounts_and_increase()
    {
        Assert.Equal(1150m, BudgetMath.Natural("Revenue", 50, 1200));
        Assert.Equal(-1150m, BudgetMath.Natural("Expense", 50, 1200));
        Assert.Equal(366.663m, BudgetMath.Increase(333.33m, 10, 3));
        Assert.Equal(0.556m, BudgetMath.Increase(0.5555m, 0, 3));     // half away from zero
        Assert.Equal(-90m, BudgetMath.Increase(-100, -10, 3));
        Assert.True(BudgetMath.IsValidAmount(-12.5m, 3));
        Assert.False(BudgetMath.IsValidAmount(1.2345m, 3));
    }

    [Theory]
    [InlineData("month", 5, 5, 5)]
    [InlineData("ytd", 5, 1, 5)]
    [InlineData("year", 5, 1, 12)]
    public void Report_modes_cover_their_months(string mode, int month, int first, int last)
    {
        Assert.Equal((first, last), BudgetMath.Months(mode, month));
        decimal[] months = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
        Assert.Equal(Enumerable.Range(first, last - first + 1).Sum(), BudgetMath.Sum(months, first, last));
    }

    [Fact]
    public void Duplicates_and_parent_overlaps_are_found_per_cost_centre()
    {
        // 1 → 10 → 100 (parent chain); 2 unrelated.
        long? Parent(long id) => id switch { 100 => 10, 10 => 1, _ => null };
        Assert.Null(BudgetMath.FindConflict([(100, null), (2, null), (10, 7)], Parent));                // parent with another cost centre: fine
        Assert.Equal((1, (long?)null), BudgetMath.FindConflict([(100, 7), (100, 7)], Parent));        // same account and cost centre twice
        Assert.Equal((0, (long?)1), BudgetMath.FindConflict([(100, null), (1, null)], Parent));       // a sub-account and its grand-parent
        Assert.Equal((1, (long?)10), BudgetMath.FindConflict([(10, 3), (100, 3)], Parent));
    }
}
