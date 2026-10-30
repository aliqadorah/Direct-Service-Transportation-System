using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using DirectService.Transportation.Web.Client.Ui;
using DirectService.Shared.UI.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using static DirectService.Transportation.UI.Tests.BudgetPageTests;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner request 26 Oct 2026: the budget grid with every account (DT-265) — filters that never lose hidden amounts, Distribute / Fill / Clear,
/// only rows with amounts saved, lines kept from earlier — and the profit and loss by section with "Columns by" and the drill-down (DT-266,
/// DT-267), the account statement opened for "no cost centre", the profit and loss section of an account (DT-264).
/// </summary>
public sealed class BudgetGridProfitLossPageTests : PageTestBase
{
    private static readonly string[] CentreAccounts = ["e2"];

    public BudgetGridProfitLossPageTests()
    {
        Options(this, Respond);
        Respond("accounting/budgets/b1", Budget());
    }

    private IRenderedComponent<BudgetEditor> Editor()
    {
        var cut = Render<BudgetEditor>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "b1"));
        cut.WaitForAssertion(() => Assert.Contains(L["bud.lines"], cut.Markup));
        return cut;
    }

    private string Cell(string code, int month) => $"input[aria-label='{L.F("bud.cell", code, L["bud.m" + month])}']";

    private static string? Value(IRenderedComponent<BudgetEditor> cut, string selector) => cut.Find(selector).GetAttribute("value");

    /// <summary>Chooses the option with that text (empty = the empty option) in the select with that label (option values are indices).</summary>
    private void Select<T>(IRenderedComponent<T> cut, string label, string option) where T : IComponent
    {
        var id = cut.FindAll("label").First(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).GetAttribute("for");
        Act(() =>
        {
            var select = cut.Find("#" + id);
            var value = option.Length == 0 ? "" : select.QuerySelectorAll("option").First(o => o.TextContent.Trim() == option).GetAttribute("value");
            select.Change(value);
        });
    }

    private void Click<T>(IRenderedComponent<T> cut, string text) where T : IComponent =>
        Act(() => (cut.FindAll("button").FirstOrDefault(b => b.TextContent.Trim() == text) ?? cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal))).Click());

    private string SaveBody(IRenderedComponent<BudgetEditor> cut)
    {
        Click(cut, L["ui.save"]);
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT" && r.Path == "accounting/budgets/b1"));
        return Requests.Last(r => r.Method == "PUT").Body!;
    }

    // ------------------------------------------------------------------ budget grid

    [Fact]
    public void Budget_grid_shows_every_income_and_expense_account_with_group_subtotals_and_saves_only_rows_with_amounts()
    {
        var cut = Editor();
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains(L["bud.addLine"], StringComparison.Ordinal));
        Assert.Equal(3, cut.FindAll("input[data-c='0']").Count);                // 4102, 5202, 5203 — not the balance-sheet account
        Assert.DoesNotContain("1106", Text(cut));
        Assert.Equal(3, cut.FindAll("tbody tr.grp").Count);                     // headings 4, 5, 52 (not editable)
        Assert.Contains(cut.FindAll("tbody tr.grp"), r => r.TextContent.Contains("52", StringComparison.Ordinal) && r.TextContent.Contains(Fmt.Money(300), StringComparison.Ordinal));
        Assert.NotNull(cut.Find("button[data-shortcut='Ctrl S']"));            // Ctrl+S (DT-233)
        Assert.Equal(Fmt.Money(1000), Value(cut, Cell("4102", 1)));

        Act(() => cut.Find(Cell("5203", 2)).Change("50"));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("tbody tr.grp"), r => r.TextContent.Contains(Fmt.Money(350), StringComparison.Ordinal)));   // 52: 300 + 50
        Assert.Contains(Fmt.Money(350), cut.Find("tfoot").TextContent);       // whole budget: expenses

        var body = SaveBody(cut);
        Assert.Contains("\"accountId\":\"e2\",\"costCentreId\":null,\"months\":[0,50,0,0,0,0,0,0,0,0,0,0]", body, StringComparison.Ordinal);
        Assert.Contains("\"accountId\":\"e1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"accountId\":\"r1\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"accountId\":\"b1\"", body, StringComparison.Ordinal);   // empty rows are not sent
        Assert.DoesNotContain("\"accountId\":\"e0\"", body, StringComparison.Ordinal);   // headings never
    }

    [Fact]
    public void Filters_only_choose_the_rows_shown_and_hidden_amounts_are_saved()
    {
        Respond("accounting/cost-centres/options", new[] { new MasterOption("cc1", "CC1 – North") });
        Respond("accounting/budgets/cost-centre-accounts", CentreAccounts);
        var cut = Editor();

        Select(cut, L["bud.typeFilter"], L["bud.type.bs"]);
        Assert.Single(cut.FindAll("input[data-c='0']"));
        Act(() => cut.Find(Cell("1106", 1)).Change("70"));

        Select(cut, L["bud.typeFilter"], L["bud.type.existing"]);                 // 4102, 5202 (saved) and 1106 (typed in)
        Assert.Equal(3, cut.FindAll("input[data-c='0']").Count);

        Select(cut, L["bud.typeFilter"], L["bud.type.all"]);
        Select(cut, L["je.costCentre"], "CC1 – North");                                   // accounts posted with CC1: 5203 only
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("input[data-c='0']")));
        Assert.NotNull(cut.Find(Cell("5203", 1)));
        Assert.Contains(Requests, r => r.Path == "accounting/budgets/cost-centre-accounts?costCentre=cc1&year=2026");

        Select(cut, L["je.costCentre"], "");
        Act(() => cut.Find("#bud-search").Input("520"));                          // search by code
        Assert.Equal(2, cut.FindAll("input[data-c='0']").Count);
        Act(() => cut.Find("#bud-search").Input("Commun"));                       // and by name
        Assert.Single(cut.FindAll("input[data-c='0']"));

        var body = SaveBody(cut);                                                  // the rows hidden now keep their amounts
        Assert.Contains("\"accountId\":\"r1\",\"costCentreId\":null,\"months\":[1000,1000,0,0,0,0,0,0,0,0,0,0]", body, StringComparison.Ordinal);
        Assert.Contains("\"accountId\":\"e1\",\"costCentreId\":null,\"months\":[300,0,0,0,0,0,0,0,0,0,0,0]", body, StringComparison.Ordinal);
        Assert.Contains("\"accountId\":\"b1\",\"costCentreId\":null,\"months\":[70,0,0,0,0,0,0,0,0,0,0,0]", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Distribute_fill_and_clear_change_only_the_selected_rows()
    {
        var cut = Editor();
        Act(() => cut.Find($"input[aria-label='{L.F("bud.selectRow", "5203")}']").Change(true));
        Act(() => cut.Find("#bud-yearly").Change("1000"));
        Click(cut, L["bud.distribute"]);
        Assert.Equal(Fmt.Money(83.333m), Value(cut, Cell("5203", 1)));
        Assert.Equal(Fmt.Money(83.337m), Value(cut, Cell("5203", 12)));          // December takes the rounding difference
        Assert.Equal(Fmt.Money(300), Value(cut, Cell("5202", 1)));                // not selected: unchanged
        Assert.Null(Value(cut, Cell("5202", 2)));

        Act(() => cut.Find($"input[aria-label='{L.F("bud.selectRow", "5202")}']").Change(true));
        Assert.Contains(L.F("bud.selected", 2), Text(cut));
        Click(cut, L["bud.fill"]);                                                 // from January (the default)
        Assert.Equal(Fmt.Money(300), Value(cut, Cell("5202", 7)));
        Assert.Equal(Fmt.Money(83.333m), Value(cut, Cell("5203", 12)));

        Act(() => cut.Find($"input[aria-label='{L.F("bud.selectRow", "5202")}']").Change(false));
        Click(cut, L["bud.clearRows"]);
        Assert.Null(Value(cut, Cell("5203", 1)));
        Assert.Equal(Fmt.Money(300), Value(cut, Cell("5202", 12)));

        var body = SaveBody(cut);
        Assert.Contains("\"accountId\":\"e1\",\"costCentreId\":null,\"months\":[300,300,300,300,300,300,300,300,300,300,300,300]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"accountId\":\"e2\"", body, StringComparison.Ordinal);   // cleared: not saved
    }

    /// <summary>
    /// Owner finding 27 Oct 2026 (DT-268): the top card flows at its full height (never a strip with its own scrollbar), the filters sit in
    /// one row aligned at the top (a help text under one field does not move the others), and an empty filter result shows its message in
    /// the grid's card, right under the filters.
    /// </summary>
    [Fact]
    public void Budget_layout_top_card_flows_filters_align_and_an_empty_result_sits_in_the_grid_card()
    {
        Respond("accounting/cost-centres/options", new[] { new MasterOption("cc1", "CC1 – North") });
        Respond("accounting/budgets/cost-centre-accounts", Array.Empty<string>());
        var cut = Editor();
        var form = cut.Find("form");
        Assert.Contains("flow", form.ClassList);
        Assert.DoesNotContain("fill", form.ClassList);
        var filters = cut.Find(".bud-filters");
        Assert.Equal(3, filters.QuerySelectorAll("select, input[type=search]").Length);   // account type, cost centre, search in one row

        Select(cut, L["je.costCentre"], "CC1 – North");                               // no account posted with it
        cut.WaitForAssertion(() => Assert.Contains(L["bud.noRows"], cut.Markup));
        Assert.Contains(L.F("bud.centreHelp", 2025, 2026), cut.Find(".bud-filters").TextContent);   // the help stays under its field, in the row
        var box = cut.Find("section.bud-box");
        Assert.Contains(L["bud.noRows"], box.TextContent);
        Assert.Empty(cut.FindAll("table.bud-grid"));
    }

    [Fact]
    public void A_cell_that_is_not_an_amount_is_marked_and_blocks_saving()
    {
        var cut = Editor();
        Act(() => cut.Find(Cell("5203", 3)).Change("12,5x"));
        Assert.Equal("true", cut.Find(Cell("5203", 3)).GetAttribute("aria-invalid"));
        Assert.Equal("12,5x", Value(cut, Cell("5203", 3)));
        Click(cut, L["ui.save"]);
        Assert.DoesNotContain(Requests, r => r.Method == "PUT");
        Act(() => cut.Find(Cell("5203", 3)).Change("12.5"));
        Assert.Null(cut.Find(Cell("5203", 3)).GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Lines_per_cost_centre_or_on_a_group_are_kept_and_lock_the_accounts_below_the_group()
    {
        var centre = new Ref("cc1", "CC1 – North");
        Respond("accounting/budgets/b1", Budget() with
        {
            Lines =
            [
                new BudgetLineResponse(Revenue, "Revenue", false, null, Months(1000, 1000), 2000),
                new BudgetLineResponse(Admin, "Expense", true, null, Months(400), 400),
                new BudgetLineResponse(Revenue, "Revenue", false, centre, Months(5), 5),
            ],
        });
        var cut = Editor();
        Assert.Contains(L.F("bud.keptHelp", 2, Fmt.Money(405)), Text(cut));
        Assert.Empty(cut.FindAll(Cell("5202", 1)));                                // under 52, which has its own line
        Assert.NotNull(cut.Find(Cell("4102", 1)));
        Assert.Contains(Fmt.Money(2005), Text(cut));                              // revenue: the grid row + the kept line

        var body = SaveBody(cut);
        Assert.Contains("\"accountId\":\"e0\",\"costCentreId\":null,\"months\":[400,0,0,0,0,0,0,0,0,0,0,0]", body, StringComparison.Ordinal);
        Assert.Contains("\"accountId\":\"r1\",\"costCentreId\":\"cc1\",\"months\":[5,0,0,0,0,0,0,0,0,0,0,0]", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ profit and loss

    private static readonly AccountRef Rev = new("a4", "4", "الإيرادات", "Revenue");
    private static readonly AccountRef Transport = new("a4101", "4101", "إيرادات النقل", "Transport revenue");
    private static readonly AccountRef Costs = new("a51", "51", "تكاليف النقل المباشرة", "Direct transport costs");
    private static readonly AccountRef Wages = new("a5101", "5101", "أجور السائقين", "Driver wages");

    private static ProfitLossReport Pl(string mode, IReadOnlyList<ProfitLossColumn> columns, decimal[] sales, decimal[] cost)
    {
        ProfitLossFigure F(decimal[] a) => new(a, a.Sum());
        var gross = sales.Zip(cost, (s, c) => s - c).ToArray();
        var empty = F(new decimal[columns.Count]);
        return new ProfitLossReport(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null, null, null, [], [], sales.Sum(), cost.Sum(), gross.Sum(), null, null, null,
            mode, columns,
            [
                new ProfitLossSection(ProfitLossGroups.Sales, [new ProfitLossRow(Rev, "Revenue", 1, false, null, sales, sales.Sum()), new ProfitLossRow(Transport, "Revenue", 2, true, "a4", sales, sales.Sum())], F(sales)),
                new ProfitLossSection(ProfitLossGroups.CostOfSales, [new ProfitLossRow(Costs, "Expense", 2, false, null, cost, cost.Sum()), new ProfitLossRow(Wages, "Expense", 3, true, "a51", cost, cost.Sum())], F(cost)),
                new ProfitLossSection(ProfitLossGroups.Operating, [], empty),
                new ProfitLossSection(ProfitLossGroups.OtherIncome, [], empty),
                new ProfitLossSection(ProfitLossGroups.OtherExpense, [], empty),
            ], F(gross), F(gross));
    }

    private static readonly ProfitLossColumn Whole = new("total", "Amount", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null);

    [Fact]
    public void Profit_and_loss_shows_sections_gross_and_net_profit_and_groups_expand_and_collapse()
    {
        Respond("accounting/reports/profit-loss", Pl("none", [Whole], [1000], [300]));
        var cut = Render<ProfitLoss>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["pl.sec.Sales"], Text(cut)));
        var text = Text(cut);
        Assert.Contains(L["pl.sec.CostOfSales"], text);
        Assert.DoesNotContain(L["pl.sec.OtherIncome"], text);                     // empty optional sections are not shown
        Assert.True(text.LastIndexOf(L["pl.gross"], StringComparison.Ordinal) > text.IndexOf(L["pl.sec.CostOfSales"], StringComparison.Ordinal));   // the figure on top, then after the cost of sales
        Assert.Contains(Fmt.Money(700), text);                                     // gross and net profit
        Assert.Equal(2, cut.FindAll("tr.pl-gtot").Count);                          // a total row for each open group
        Assert.Contains("4101", text);

        Act(() => cut.Find($"button[aria-label='{L.F("acc.collapse", "4")}']").Click());
        Assert.DoesNotContain("4101", Text(cut));
        Assert.Single(cut.FindAll("tr.pl-gtot"));
        Click(cut, L["rep.collapseAll"]);
        Assert.DoesNotContain("5101", Text(cut));
        Click(cut, L["rep.expandAll"]);
        Assert.Contains("5101", Text(cut));
    }

    [Fact]
    public void Columns_by_cost_centre_have_no_cost_centre_and_an_amount_opens_the_statement_for_that_column()
    {
        Respond("accounting/cost-centres/options", new[] { new MasterOption("cc1", "CC1 – North") });
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 3, 31);
        Respond("accounting/reports/profit-loss", r => r.RequestUri!.Query.Contains("columnsBy=costCentre", StringComparison.Ordinal)
            ? Pl("costCentre", [new("cc1", "CC1 North", from, to, "cc1"), new("none", L["pl.noCostCentre"], from, to, "none")], [600, 400], [100, 200])
            : Pl("none", [Whole], [1000], [300]));
        var cut = Render<ProfitLoss>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["pl.sec.Sales"], Text(cut)));
        Select(cut, L["pl.columnsBy"], L["pl.by.costCentre"]);
        Click(cut, L["rep.run"]);
        cut.WaitForAssertion(() => Assert.Contains(L["pl.noCostCentre"], cut.Find("thead").TextContent));
        Assert.Contains(L["pl.total"], cut.Find("thead").TextContent);
        Assert.Contains(Fmt.Money(700), Text(cut));                                // net: (600 − 100) + (400 − 200)

        var label = L.F("pl.openStatement", "4101", L["pl.noCostCentre"]);
        Act(() => cut.Find($"button[aria-label='{label}']").Click());
        var uri = Services.GetRequiredService<NavigationManager>().Uri;
        Assert.Contains("accounting/statement?accounts=a4101&from=2026-01-01&to=2026-03-31&costCentre=none", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Month_columns_quick_periods_hide_zero_and_exports_send_the_same_choice()
    {
        var m1 = new ProfitLossColumn("2026-01", "Jan 2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null);
        var m2 = new ProfitLossColumn("2026-02", "Feb 2026", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), null);
        Respond("accounting/reports/profit-loss", Pl("month", [m1, m2], [100, 200], [10, 0]));
        var cut = Render<ProfitLoss>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("Feb 2026", cut.Find("thead").TextContent));
        Select(cut, L["pl.columnsBy"], L["pl.by.month"]);
        Act(() => cut.FindAll("input[type=checkbox]").First(c => c.ParentElement!.TextContent.Contains(L["pl.hideZero"], StringComparison.Ordinal)).Change(false));
        Click(cut, L["pl.q.thisYear"]);
        var year = DateTime.Now.Year;
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.Contains($"from={year}-01-01&to={year}-12-31", StringComparison.Ordinal)
            && r.Path.Contains("columnsBy=month", StringComparison.Ordinal) && r.Path.Contains("hideZero=false", StringComparison.Ordinal)));
        // A month amount opens the statement of that month.
        Act(() => cut.Find($"button[aria-label='{L.F("pl.openStatement", "5101", "Jan 2026")}']").Click());
        Assert.Contains("from=2026-01-01&to=2026-01-31", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Profit_and_loss_export_uses_the_columns_choice()
    {
        Respond("accounting/reports/profit-loss", Pl("none", [Whole], [1000], [300]));
        var cut = Render<ProfitLoss>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["pl.sec.Sales"], Text(cut)));
        Select(cut, L["pl.columnsBy"], L["pl.by.quarter"]);
        Click(cut, "Excel");
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("accounting/reports/profit-loss/export?", StringComparison.Ordinal)
            && r.Path.Contains("columnsBy=quarter", StringComparison.Ordinal) && r.Path.Contains("format=xlsx", StringComparison.Ordinal)));
    }

    // ------------------------------------------------------------------ account statement, chart

    [Fact]
    public void Account_statement_opened_for_no_cost_centre_filters_by_it()
    {
        Respond("accounting/accounts", new[] { new AccountResponse("a4101", "4101", "إيرادات النقل", "Transport revenue", "Revenue", null, null, 1, true, false, 0, 0, false, null, "Active", "A") });
        Respond("accounting/accounts/options", Array.Empty<object>());
        Respond("accounting/reports/account-statements", new AccountStatementsReport(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), null, new Ref("none", "No cost centre"), [], 0, 0, 0, 0));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/statement?accounts=a4101&from=2026-01-01&to=2026-03-31&costCentre=none");
        var cut = Render<AccountStatement>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("accounting/reports/account-statements?", StringComparison.Ordinal)
            && r.Path.Contains("costCentre=none", StringComparison.Ordinal) && r.Path.Contains("from=2026-01-01", StringComparison.Ordinal)));
        Assert.Contains(L["pl.noCostCentre"], cut.Markup);                          // the filter shows it
    }

    [Fact]
    public void Chart_account_form_sets_the_profit_and_loss_section()
    {
        Respond("accounting/accounts", r => r.Method == HttpMethod.Put
            ? new AccountResponse("x2", "5101", "أجور السائقين", "Driver wages", "Expense", "x1", "51", 2, true, false, 0, 0, false, null, "Active", "B", false, ProfitLossGroups.OtherExpense)
            : new[]
            {
                new AccountResponse("x1", "51", "تكاليف النقل المباشرة", "Direct transport costs", "Expense", null, null, 1, false, true, 0, 0, false, null, "Active", "A", false, ProfitLossGroups.CostOfSales),
                new AccountResponse("x2", "5101", "أجور السائقين", "Driver wages", "Expense", "x1", "51", 2, true, false, 0, 0, false, null, "Active", "A"),
            });
        Respond("accounting/accounts/x2/history", Array.Empty<object>());
        var cut = Render<ChartOfAccounts>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("أجور السائقين", cut.Markup));
        Act(() => cut.FindAll("tbody tr").First(r => r.TextContent.Contains("5101", StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["acc.plGroup"], cut.Markup));
        Assert.Contains(L.F("acc.plGroupInherit", L["acc.pl.CostOfSales"]), cut.Markup);   // inherited from 51
        Select(cut, L["acc.plGroup"], L["acc.pl.OtherExpense"]);
        Act(() => cut.FindAll("form").First(f => f.TextContent.Contains(L["acc.plGroup"], StringComparison.Ordinal)).Submit());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT"));
        Assert.Contains("\"profitLossGroup\":\"OtherExpense\"", Requests.Single(r => r.Method == "PUT").Body!, StringComparison.Ordinal);
    }
}
