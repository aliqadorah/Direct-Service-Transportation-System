using DirectService.Accounting.Application.Posting;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Budgets;
using DirectService.Transportation.Contracts.Accounting;

namespace DirectService.Transportation.Application.Tests;

/// <summary>Owner request 26 Oct 2026: budget distribution and directions (DT-265), profit and loss sections (DT-264), Arabic tax names (DT-263).</summary>
public sealed class BudgetProfitLossUnitTests
{
    [Theory]
    [InlineData(1000, 83.333, 83.337)]     // 11 × 83.333 = 916.663; December takes the rest
    [InlineData(1200, 100, 100)]
    [InlineData(0.01, 0.001, -0.001)]      // rounds up: December goes below
    [InlineData(-100, -8.333, -8.337)]
    public void Distribute_spreads_a_yearly_amount_and_December_takes_the_rounding(double yearlyValue, double monthValue, double decemberValue)
    {
        var (yearly, month, december) = ((decimal)yearlyValue, (decimal)monthValue, (decimal)decemberValue);
        var months = BudgetMath.Distribute(yearly, 3);
        Assert.Equal(12, months.Length);
        Assert.All(months.Take(11), m => Assert.Equal(month, m));
        Assert.Equal(december, months[11]);
        Assert.Equal(yearly, months.Sum());
    }

    [Fact]
    public void Natural_direction_of_balance_sheet_accounts()
    {
        Assert.Equal(70m, BudgetMath.Natural("Asset", 100, 30));
        Assert.Equal(70m, BudgetMath.Natural("Liability", 30, 100));
        Assert.Equal(70m, BudgetMath.Natural("Equity", 30, 100));
        Assert.True(BudgetService.IsBudgetType("Asset"));
        Assert.False(BudgetService.IsBudgetType("Other"));
    }

    [Fact]
    public void Profit_and_loss_sections_by_type_and_their_order()
    {
        Assert.Equal([ProfitLossGroups.Sales, ProfitLossGroups.OtherIncome], ProfitLossGroups.For("Revenue"));
        Assert.Equal([ProfitLossGroups.CostOfSales, ProfitLossGroups.Operating, ProfitLossGroups.OtherExpense], ProfitLossGroups.For("Expense"));
        Assert.Empty(ProfitLossGroups.For("Asset"));
        Assert.Equal((ProfitLossGroups.Sales, ProfitLossGroups.Operating), (ProfitLossGroups.Default("Revenue"), ProfitLossGroups.Default("Expense")));
        Assert.Null(ProfitLossGroups.Default("Liability"));
        Assert.Equal(ProfitLossGroups.Sales, ProfitLossGroups.Order[0]);
        Assert.Equal(1, ProfitLossGroups.Order.ToList().IndexOf(ProfitLossGroups.CostOfSales));   // gross profit comes right after it
    }

    [Fact]
    public void Template_has_cost_of_sales_and_the_new_Arabic_tax_names()
    {
        Assert.Equal(ProfitLossGroups.CostOfSales, ChartTemplate.Accounts.Single(a => a.Code == "51").ProfitLossGroup);
        Assert.All(ChartTemplate.Accounts.Where(a => a.Code != "51"), a => Assert.Null(a.ProfitLossGroup));
        Assert.Equal("ضريبة مبيعات غير قابلة للخصم", ChartTemplate.Accounts.Single(a => a.Code == "5207").NameAr);
        Assert.Equal("ضريبة مبيعات غير قابلة للخصم", ChartTemplate.TaxAccounts.Single(a => a.Type == "Expense").NameAr);
        Assert.Contains("مصروف ضريبة المبيعات غير القابلة للاسترداد", ChartTemplate.FormerNamesAr);
        Assert.All(JournalTax.Defaults, d => Assert.Equal($"ضريبة المبيعات العامة {d.Rate:0}%", d.NameAr));
        Assert.Equal("ضريبة المبيعات العامة 16% على السطر 2 / VAT 16% on line 2", JournalTax.Note(JournalTax.Defaults[^1].NameAr, "VAT 16%", "VAT_JO_16", 2));
    }
}
