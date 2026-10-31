using System.Net.Http.Json;
using System.Text;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Database.Migrations;
using Microsoft.EntityFrameworkCore;
using static DirectService.Transportation.Api.Tests.PartBSupport;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner request 26 Oct 2026: Arabic tax names (DT-263), the profit and loss section of an account (DT-264), the budget screen's data —
/// every account, the cost-centre filter, only lines with amounts (DT-265) — and the profit and loss by section with "Columns by", the
/// "No cost centre" column, the drill-down filters and the exports (DT-266, DT-267).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BudgetProfitLossTests(ApiFactory f)
{
    private const string OldVat = "مصروف ضريبة المبيعات غير القابلة للاسترداد";

    private static readonly int Y = Today.Year - 1;

    private static DateOnly Day(int month, int day) => new(Y, month, day);

    private static AccountRequest Edit(AccountResponse a, string? group, string? nameAr = null, string? nameEn = null) =>
        new(a.Code, nameAr ?? a.NameAr, nameEn ?? a.NameEn, a.AccountType, a.ParentId, a.IsPostable, a.IsControlAccount, a.ControlPartyType, a.RowVersion, a.IsTaxAccount, group);

    // ------------------------------------------------------------------ DT-263 / DT-264: template, migration data steps

    [Fact]
    public async Task New_companies_get_the_new_Arabic_tax_names_and_51_as_cost_of_sales()
    {
        var (o, a) = await CompanyAsync(f);
        Assert.Equal(ProfitLossGroups.CostOfSales, a["51"].ProfitLossGroup);
        Assert.Null(a["5101"].ProfitLossGroup);                                   // inherits from 51
        Assert.Null(a["52"].ProfitLossGroup);                                     // default: operating expenses
        Assert.Equal("ضريبة مبيعات غير قابلة للخصم", a["5207"].NameAr);
        var codes = await OkAsync<List<TaxCodeResponse>>(await o.Client.GetAsync(U(o, "/accounting/tax-codes")));
        Assert.Equal("ضريبة المبيعات العامة 16%", codes.Single(c => c.Code == "VAT_JO_16").NameAr);
        Assert.All(codes, c => Assert.StartsWith("ضريبة المبيعات العامة ", c.NameAr, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Migration_renames_only_texts_that_are_still_the_old_default()
    {
        var (o, a) = await CompanyAsync(f);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        async Task<TaxCodeResponse> Code(string code, string nameAr) =>
            await OkAsync<TaxCodeResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/tax-codes"), new TaxCodeRequest(code + tag, 7, nameAr, "VAT 7%", null, null)));
        var old = await Code("O", "ضريبة المبيعات 7%");
        var mine = await Code("M", "ضريبة المبيعات 7% خاصة");
        async Task<AccountResponse> Account(string code, string nameAr) => await OkAsync<AccountResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts"),
            new AccountRequest(code, nameAr, null, "Expense", a["52"].Id, true, false, null, null)));
        var oldVat = await Account("5281", OldVat);
        var myVat = await Account("5282", "ضريبتي غير المستردة");
        // 51 without a section: back to cost of sales while it is the template's own; renamed (both names) it stays as it is.
        await OkAsync<AccountResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/accounts/{a["51"].Id}"), Edit(a["51"], null)));
        var (o2, a2) = await CompanyAsync(f);
        await OkAsync<AccountResponse>(await o2.Client.PutAsJsonAsync(U(o2, $"/accounting/accounts/{a2["51"].Id}"), Edit(a2["51"], null, "تكاليفي", "My costs")));

        await f.DbAsync(async db =>
        {
            foreach (var sql in new[] { Rev6_BudgetProfitLoss.CostOfSalesSql, Rev6_BudgetProfitLoss.TaxCodeNamesSql, Rev6_BudgetProfitLoss.VatExpensesNameSql })
                await db.Database.ExecuteSqlRawAsync(sql);
            return 0;
        });

        var codes = await OkAsync<List<TaxCodeResponse>>(await o.Client.GetAsync(U(o, "/accounting/tax-codes")));
        Assert.Equal("ضريبة المبيعات العامة 7%", codes.Single(c => c.Id == old.Id).NameAr);
        Assert.Equal("ضريبة المبيعات 7% خاصة", codes.Single(c => c.Id == mine.Id).NameAr);
        var accounts = (await OkAsync<List<AccountResponse>>(await o.Client.GetAsync(U(o, "/accounting/accounts")))).ToDictionary(x => x.Code);
        Assert.Equal("ضريبة مبيعات غير قابلة للخصم", accounts[oldVat.Code].NameAr);
        Assert.Equal("ضريبتي غير المستردة", accounts[myVat.Code].NameAr);
        Assert.Equal(ProfitLossGroups.CostOfSales, accounts["51"].ProfitLossGroup);
        var other = (await OkAsync<List<AccountResponse>>(await o2.Client.GetAsync(U(o2, "/accounting/accounts")))).Single(x => x.Code == "51");
        Assert.Null(other.ProfitLossGroup);
    }

    [Fact]
    public async Task Profit_and_loss_section_must_fit_the_account_type()
    {
        var (o, a) = await CompanyAsync(f);
        Assert.Equal("validation.format", await (await o.Client.PutAsJsonAsync(U(o, $"/accounting/accounts/{a["4105"].Id}"), Edit(a["4105"], ProfitLossGroups.CostOfSales))).ErrorCodeAsync());
        Assert.Equal("validation.format", await (await o.Client.PutAsJsonAsync(U(o, $"/accounting/accounts/{a["1106"].Id}"), Edit(a["1106"], ProfitLossGroups.Sales))).ErrorCodeAsync());
        var saved = await OkAsync<AccountResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/accounts/{a["4105"].Id}"), Edit(a["4105"], ProfitLossGroups.OtherIncome)));
        Assert.Equal(ProfitLossGroups.OtherIncome, saved.ProfitLossGroup);
    }

    // ------------------------------------------------------------------ DT-265: budget screen data

    [Fact]
    public async Task Budget_grid_has_every_account_keeps_only_lines_with_amounts_and_takes_balance_sheet_accounts()
    {
        var (o, a) = await CompanyAsync(f);
        var grid = await OkAsync<List<BudgetGridAccount>>(await o.Client.GetAsync(U(o, "/accounting/budgets/grid-accounts")));
        Assert.Equal(a.Count, grid.Count);
        Assert.True(grid.Single(g => g.Account.Code == "51").IsGroup);
        var rent = grid.Single(g => g.Account.Code == "5202");
        Assert.False(rent.IsGroup);
        Assert.Equal((a["52"].Id, 3), (rent.ParentId, rent.Level));
        Assert.Contains(grid, g => g.AccountType == "Asset" && !g.IsGroup);

        var b = await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/budgets"), new CreateBudgetRequest(Today.Year, "Grid " + Guid.NewGuid().ToString("N")[..6])));
        var months = new decimal[12];
        months[0] = 100;
        b = await OkAsync<BudgetResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}"), new BudgetRequest(b.Name,
            [new BudgetLineRequest(a["5202"].Id, null, months), new BudgetLineRequest(a["5203"].Id, null, new decimal[12]), new BudgetLineRequest(a["1106"].Id, null, months)], b.RowVersion)));
        Assert.Equal(["1106", "5202"], b.Lines.Select(l => l.Account.Code).Order(StringComparer.Ordinal));   // the all-zero line is not kept
        Assert.Equal((0m, 100m), (b.RevenueTotal, b.ExpenseTotal));                                            // the asset line is not an expense

        // Budget vs Actual keeps working: revenue and expenses only.
        b = await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(b.RowVersion)));
        var r = await OkAsync<BudgetVsActualReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/budget-vs-actual?year={Today.Year}&month=1&mode=month")));
        Assert.Equal(100m, r.Expenses.Budget);
        Assert.DoesNotContain(r.Rows, x => x.Account.Code == "1106");
    }

    [Fact]
    public async Task Budget_cost_centre_filter_lists_accounts_posted_with_it_in_the_year_or_the_year_before()
    {
        var (o, a) = await CompanyAsync(f);
        var cc = await OkAsync<CostCentreResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/cost-centres"), new CostCentreRequest("CF1", "Branch", null)));
        await PostAsync(o, new DateOnly(Today.Year - 1, 3, 1), a["5203"], a["110101"], 10, cc.Id);   // the year before: counted
        await PostAsync(o, new DateOnly(Today.Year - 2, 3, 1), a["5205"], a["110101"], 10, cc.Id);   // two years before: not
        await PostAsync(o, Today, a["5204"], a["110101"], 10);                                       // no cost centre: not
        var ids = await OkAsync<List<string>>(await o.Client.GetAsync(U(o, $"/accounting/budgets/cost-centre-accounts?costCentre={cc.Id}&year={Today.Year}")));
        Assert.Contains(a["5203"].Id, ids);
        Assert.Contains(a["110101"].Id, ids);
        Assert.DoesNotContain(a["5205"].Id, ids);
        Assert.DoesNotContain(a["5204"].Id, ids);
        Assert.Equal("validation.required", await (await o.Client.GetAsync(U(o, $"/accounting/budgets/cost-centre-accounts?year={Today.Year}"))).ErrorCodeAsync());
    }

    // ------------------------------------------------------------------ DT-266 / DT-267: profit and loss

    private sealed record World(SignedInUser O, Dictionary<string, AccountResponse> A, CostCentreResponse Cc);

    /// <summary>
    /// Last year: sales 1,000 (Jan, CC1) + 500 (Apr); cost of sales 300 (Feb, CC1); operating 200 (Apr); other income 50 (Feb, 4105);
    /// other expense 20 (Jan, CC1, 5206). Gross 1,200, net 1,030.
    /// </summary>
    private async Task<World> WorldAsync()
    {
        var (o, a) = await CompanyAsync(f);
        var cc = await OkAsync<CostCentreResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/cost-centres"), new CostCentreRequest("CC1", "North", null)));
        await OkAsync<AccountResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/accounts/{a["4105"].Id}"), Edit(a["4105"], ProfitLossGroups.OtherIncome)));
        await OkAsync<AccountResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/accounts/{a["5206"].Id}"), Edit(a["5206"], ProfitLossGroups.OtherExpense)));
        await PostAsync(o, Day(1, 15), a["110101"], a["4101"], 1000, cc.Id);
        await PostAsync(o, Day(4, 10), a["110101"], a["4102"], 500);
        await PostAsync(o, Day(2, 20), a["5101"], a["110101"], 300, cc.Id);
        await PostAsync(o, Day(4, 10), a["5202"], a["110101"], 200);
        await PostAsync(o, Day(2, 1), a["110101"], a["4105"], 50);
        await PostAsync(o, Day(1, 31), a["5206"], a["110101"], 20, cc.Id);
        return new World(o, a, cc);
    }

    private static async Task<ProfitLossReport> PlAsync(World w, string query) =>
        await OkAsync<ProfitLossReport>(await w.O.Client.GetAsync(U(w.O, $"/accounting/reports/profit-loss?from={D(Day(1, 1))}&to={D(Day(4, 30))}" + query)));

    private static ProfitLossSection Sec(ProfitLossReport r, string group) => r.Sections!.Single(s => s.Group == group);

    /// <summary>Every row, section and result: the total is the sum of its columns.</summary>
    private static void TotalsAddUp(ProfitLossReport r)
    {
        foreach (var s in r.Sections!)
        {
            Assert.All(s.Rows, x => Assert.Equal(x.Amounts.Sum(), x.Total));
            Assert.Equal(s.Total.Amounts.Sum(), s.Total.Total);
        }
        Assert.Equal(r.GrossProfit!.Amounts.Sum(), r.GrossProfit.Total);
        Assert.Equal(r.Net!.Amounts.Sum(), r.Net.Total);
        Assert.Equal(r.NetProfit, r.Net.Total);
    }

    [Fact]
    public async Task Profit_and_loss_by_section_with_group_totals_gross_and_net_profit()
    {
        var w = await WorldAsync();
        var r = await PlAsync(w, "");
        Assert.Equal("none", r.ColumnsBy);
        Assert.Single(r.Columns!);
        Assert.Equal([ProfitLossGroups.Sales, ProfitLossGroups.CostOfSales, ProfitLossGroups.Operating, ProfitLossGroups.OtherIncome, ProfitLossGroups.OtherExpense],
            r.Sections!.Select(s => s.Group));
        Assert.Equal(1500m, Sec(r, ProfitLossGroups.Sales).Total.Total);
        Assert.Equal(300m, Sec(r, ProfitLossGroups.CostOfSales).Total.Total);
        Assert.Equal(1200m, r.GrossProfit!.Total);
        Assert.Equal(200m, Sec(r, ProfitLossGroups.Operating).Total.Total);
        Assert.Equal(50m, Sec(r, ProfitLossGroups.OtherIncome).Total.Total);
        Assert.Equal(20m, Sec(r, ProfitLossGroups.OtherExpense).Total.Total);
        Assert.Equal(1030m, r.Net!.Total);
        Assert.Equal((1550m, 520m, 1030m), (r.TotalRevenue, r.TotalExpenses, r.NetProfit));   // by type, as before

        // Group rows total their sub-accounts of the section: "4" is 1,500 under sales and 50 under other income; "5" is split too.
        var sales = Sec(r, ProfitLossGroups.Sales).Rows;
        Assert.Equal(1500m, sales.Single(x => x.Account.Code == "4").Total);
        Assert.False(sales.Single(x => x.Account.Code == "4").IsLeaf);
        Assert.Equal(sales.Single(x => x.Account.Code == "4").Account.Id, sales.Single(x => x.Account.Code == "4101").ParentId);
        Assert.DoesNotContain(sales, x => x.Account.Code is "4105" or "4103");   // other income; zero rows hidden
        Assert.Equal(50m, Sec(r, ProfitLossGroups.OtherIncome).Rows.Single(x => x.Account.Code == "4").Total);
        Assert.Equal(300m, Sec(r, ProfitLossGroups.CostOfSales).Rows.Single(x => x.Account.Code == "51").Total);
        Assert.Equal(300m, Sec(r, ProfitLossGroups.CostOfSales).Rows.Single(x => x.Account.Code == "5").Total);
        Assert.Equal(200m, Sec(r, ProfitLossGroups.Operating).Rows.Single(x => x.Account.Code == "52").Total);
        Assert.DoesNotContain(Sec(r, ProfitLossGroups.Operating).Rows, x => x.Account.Code == "5206");
        TotalsAddUp(r);

        // Zero rows on request.
        var all = await PlAsync(w, "&hideZero=false");
        Assert.Contains(Sec(all, ProfitLossGroups.Sales).Rows, x => x.Account.Code == "4103" && x.Total == 0);
        Assert.Equal(1030m, all.Net!.Total);

        // The comparison period (one amount): the same accounts a year earlier have nothing.
        var cmp = await PlAsync(w, $"&compareFrom={D(Day(1, 1).AddYears(-1))}&compareTo={D(Day(4, 30).AddYears(-1))}");
        Assert.Equal(0m, cmp.Net!.Compare);
        Assert.Equal(0m, Sec(cmp, ProfitLossGroups.Sales).Rows.Single(x => x.Account.Code == "4101").CompareAmount);
    }

    [Fact]
    public async Task Profit_and_loss_columns_by_month_quarter_year_and_cost_centre_with_no_cost_centre()
    {
        var w = await WorldAsync();

        var month = await PlAsync(w, "&columnsBy=month");
        Assert.Equal([$"{Y}-01", $"{Y}-02", $"{Y}-03", $"{Y}-04"], month.Columns!.Select(c => c.Key));
        Assert.Equal((Day(2, 1), Day(2, 28 + (DateTime.IsLeapYear(Y) ? 1 : 0))), (month.Columns![1].From, month.Columns[1].To));
        Assert.Equal([1000m, 0m, 0m, 500m], Sec(month, ProfitLossGroups.Sales).Total.Amounts);
        Assert.Equal([980m, -250m, 0m, 300m], month.Net!.Amounts);
        TotalsAddUp(month);

        var quarter = await PlAsync(w, "&columnsBy=quarter");
        Assert.Equal([$"{Y}-Q1", $"{Y}-Q2"], quarter.Columns!.Select(c => c.Key));
        Assert.Equal(Day(4, 30), quarter.Columns![1].To);                 // the last quarter is cut to the period
        Assert.Equal([730m, 300m], quarter.Net!.Amounts);
        TotalsAddUp(quarter);

        var year = await PlAsync(w, "&columnsBy=year");
        Assert.Equal([$"{Y}"], year.Columns!.Select(c => c.Key));
        Assert.Equal(1030m, year.Net!.Amounts.Single());
        TotalsAddUp(year);

        var centre = await PlAsync(w, "&columnsBy=costCentre");
        Assert.Equal([w.Cc.Id, LedgerNone], centre.Columns!.Select(c => c.Key));
        Assert.Equal([w.Cc.Id, LedgerNone], centre.Columns!.Select(c => c.CostCentre));
        Assert.Equal([1000m, 500m], Sec(centre, ProfitLossGroups.Sales).Total.Amounts);
        Assert.Equal([680m, 350m], centre.Net!.Amounts);
        TotalsAddUp(centre);

        // The report for "no cost centre" only: one column.
        var none = await PlAsync(w, "&columnsBy=costCentre&costCentre=none");
        Assert.Equal([LedgerNone], none.Columns!.Select(c => c.Key));
        Assert.Equal(350m, none.Net!.Total);
        Assert.Equal("none", none.CostCentre!.Id);

        Assert.Equal("validation.format", await (await w.O.Client.GetAsync(U(w.O, "/accounting/reports/profit-loss?columnsBy=week"))).ErrorCodeAsync());
    }

    private const string LedgerNone = "none";

    [Fact]
    public async Task Account_statement_filters_by_the_columns_cost_centre_including_no_cost_centre()
    {
        var w = await WorldAsync();
        async Task<AccountStatementsReport> Statement(string account, string centre, DateOnly from, DateOnly to) =>
            await OkAsync<AccountStatementsReport>(await w.O.Client.GetAsync(U(w.O,
                $"/accounting/reports/account-statements?accounts={w.A[account].Id}&from={D(from)}&to={D(to)}&costCentre={centre}")));
        // 110101 has lines with CC1 (1,000 − 300 − 20) and without (500 − 200 + 50).
        var none = await Statement("110101", "none", Day(1, 1), Day(4, 30));
        Assert.Equal((550m, 200m), (none.TotalDebit, none.TotalCredit));
        Assert.All(none.Sections.SelectMany(s => s.Lines), l => Assert.Null(l.CostCentre));
        Assert.Equal("none", none.CostCentre!.Id);
        var cc = await Statement("110101", w.Cc.Id, Day(1, 1), Day(4, 30));
        Assert.Equal((1000m, 320m), (cc.TotalDebit, cc.TotalCredit));
        // A month column: that month's dates only.
        var feb = await Statement("5101", w.Cc.Id, Day(2, 1), Day(2, 28));
        Assert.Equal(300m, feb.TotalDebit);
        var ledger = await OkAsync<LedgerReport>(await w.O.Client.GetAsync(U(w.O, $"/accounting/reports/general-ledger?account={w.A["4102"].Id}&from={D(Day(1, 1))}&to={D(Day(4, 30))}&costCentre=none")));
        Assert.Equal(500m, ledger.TotalCredit);
    }

    [Fact]
    public async Task Profit_and_loss_exports_have_the_same_columns_and_grouping()
    {
        var w = await WorldAsync();
        async Task<string> Html(string query) =>
            Encoding.UTF8.GetString(await (await w.O.Client.GetAsync(U(w.O, $"/accounting/reports/profit-loss/export?from={D(Day(1, 1))}&to={D(Day(4, 30))}&format=html" + query))).Content.ReadAsByteArrayAsync());
        var months = await Html("&columnsBy=month");
        foreach (var text in new[] { "Sales and revenue", "Cost of sales", "Gross profit", "Operating expenses", "Other income", "Other expenses", "Net profit", $"Jan {Y}", $"Apr {Y}", "1,030.000", "980.000" })
            Assert.Contains(text, months, StringComparison.Ordinal);
        Assert.DoesNotContain("landscape", months, StringComparison.Ordinal);   // 4 months + total: portrait
        var centres = await Html("&columnsBy=costCentre");
        Assert.Contains("No cost centre", centres, StringComparison.Ordinal);
        Assert.Contains("680.000", centres, StringComparison.Ordinal);
        var wide = Encoding.UTF8.GetString(await (await w.O.Client.GetAsync(U(w.O, $"/accounting/reports/profit-loss/export?from={D(Day(1, 1))}&to={D(Day(12, 31))}&format=html&columnsBy=month"))).Content.ReadAsByteArrayAsync());
        Assert.Contains("A4 landscape", wide, StringComparison.Ordinal);       // 12 months + total
        var xlsx = await w.O.Client.GetAsync(U(w.O, $"/accounting/reports/profit-loss/export?from={D(Day(1, 1))}&to={D(Day(4, 30))}&format=xlsx&columnsBy=quarter"));
        await xlsx.EnsureOkAsync();
        Assert.Contains("spreadsheetml", xlsx.Content.Headers.ContentType!.MediaType, StringComparison.Ordinal);
        await (await w.O.Client.GetAsync(U(w.O, $"/accounting/reports/profit-loss/export?from={D(Day(1, 1))}&to={D(Day(4, 30))}&format=csv&columnsBy=costCentre"))).EnsureOkAsync();
    }
}
