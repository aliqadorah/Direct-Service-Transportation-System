using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Revision 6 part B, feature 1: the budget list, the budget page and Budget vs Actual against a fake API.</summary>
public sealed class BudgetPageTests : PageTestBase
{
    internal static readonly AccountRef Revenue = new("r1", "4102", "إيرادات أخرى", "Other revenue");
    internal static readonly AccountRef Rent = new("e1", "5202", "الإيجارات", "Rent");
    internal static readonly AccountRef Admin = new("e0", "52", "المصاريف الإدارية", "Administrative expenses");
    internal static readonly AccountRef RevenueGroup = new("g4", "4", "الإيرادات", "Revenue");
    internal static readonly AccountRef ExpenseGroup = new("g5", "5", "المصروفات", "Expenses");
    internal static readonly AccountRef Telecom = new("e2", "5203", "الاتصالات", "Communications");
    internal static readonly AccountRef Prepaid = new("b1", "1106", "مصاريف مدفوعة مقدماً", "Prepaid expenses");

    internal static decimal[] Months(decimal first, decimal second = 0)
    {
        var m = new decimal[12];
        (m[0], m[1]) = (first, second);
        return m;
    }

    internal static BudgetResponse Budget(string status = BudgetStatus.Draft) => new("b1", 2026, "موازنة 2026", status,
        status == BudgetStatus.Approved ? new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc) : null, status == BudgetStatus.Approved ? "Test User" : null,
        [new BudgetLineResponse(Revenue, "Revenue", false, null, Months(1000, 1000), 2000), new BudgetLineResponse(Rent, "Expense", false, null, Months(300), 300)],
        2000, 300, "RV1");

    internal static void Options(PageTestBase t, Action<string, object?> respond)
    {
        _ = t;
        // DT-265: every account of the chart for the grid.
        respond("accounting/budgets/grid-accounts", new[]
        {
            new BudgetGridAccount(Prepaid, "Asset", null, 1, false, true),
            new BudgetGridAccount(RevenueGroup, "Revenue", null, 1, true, true), new BudgetGridAccount(Revenue, "Revenue", "g4", 2, false, true),
            new BudgetGridAccount(ExpenseGroup, "Expense", null, 1, true, true), new BudgetGridAccount(Admin, "Expense", "g5", 2, true, true),
            new BudgetGridAccount(Rent, "Expense", "e0", 3, false, true), new BudgetGridAccount(Telecom, "Expense", "e0", 3, false, true),
        });
        respond("accounting/cost-centres/options", Array.Empty<object>());
    }

    public BudgetPageTests() => Options(this, Respond);

    [Fact]
    public void Budget_list_creates_a_budget_from_last_years_actuals_with_an_increase()
    {
        Respond("accounting/budgets", r => r.Method == HttpMethod.Post ? Budget() : new[]
        {
            new BudgetSummary("b1", 2026, "موازنة 2026", BudgetStatus.Approved, 2, 2000, 300, DateTime.UtcNow, "Test User", DateTime.UtcNow, "RV1"),
        });
        var cut = Render<Budgets>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("موازنة 2026", Text(cut)));
        Assert.Contains(L["st.approved"], cut.Markup);
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["bud.new"], StringComparison.Ordinal)).Click());
        Act(() => cut.Find("input[type=radio][value=actuals]").Change(true));
        var pct = cut.FindAll("label").Single(l => l.TextContent.Trim().StartsWith(L["bud.increase"], StringComparison.Ordinal)).GetAttribute("for");
        Act(() => cut.Find("#" + pct).Change("10"));
        Act(() => cut.Find("form#bud-new").Submit());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "accounting/budgets"));
        var body = Requests.Single(r => r.Method == "POST" && r.Path == "accounting/budgets").Body!;
        Assert.Contains("\"copyActuals\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"increasePercent\":10", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Approved_budget_is_read_only_and_can_be_returned_to_draft()
    {
        Respond("accounting/budgets/b1", Budget(BudgetStatus.Approved));
        var cut = Render<BudgetEditor>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "b1"));
        cut.WaitForAssertion(() => Assert.Contains(L["bud.returnToDraft"], cut.Markup));
        Assert.Empty(cut.FindAll("tbody input"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal) || b.TextContent.Contains(L["bud.distribute"], StringComparison.Ordinal));
        Assert.Contains(L["nav.budgetVsActual"], cut.Markup);
    }

    [Fact]
    public void Budget_vs_actual_colours_variances_and_collapses_parents()
    {
        var revenue = new BudgetVsActualRow(Revenue, "Revenue", 2, false, null, 1000, 1200, 200, 20, "favourable");
        var admin = new BudgetVsActualRow(Admin, "Expense", 1, true, null, 400, 450, 50, 12.5m, "unfavourable");
        var rent = new BudgetVsActualRow(Rent, "Expense", 2, false, "e0", 0, 450, 450, null, "unfavourable");
        Respond("accounting/reports/budget-vs-actual", new BudgetVsActualReport(new Ref("b1", "موازنة 2026"), 2026, 10, "ytd", new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 31), null,
            [revenue, admin, rent], new BudgetVsActualTotal(1000, 1200, 200, 20, "favourable"), new BudgetVsActualTotal(400, 450, 50, 12.5m, "unfavourable"),
            new BudgetVsActualTotal(600, 750, 150, 25, "favourable")));
        var cut = Render<BudgetVsActual>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("الإيجارات", cut.Markup));
        Assert.Contains("var-good", cut.Markup);
        Assert.Contains("var-bad", cut.Markup);
        Assert.Contains(L["bud.st.unfavourable"], cut.Markup);
        Assert.Contains("+" + Fmt.Money(200), Text(cut));
        Assert.Contains("+20%", Text(cut));
        Act(() => cut.Find($"button[aria-label='{L.F("acc.collapse", "52")}']").Click());
        Assert.DoesNotContain("الإيجارات", cut.Markup);
        Assert.Contains("mode=ytd", Requests.First(r => r.Path.StartsWith("accounting/reports/budget-vs-actual", StringComparison.Ordinal)).Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Budget_vs_actual_without_an_approved_budget_points_to_the_budgets()
    {
        Respond("accounting/reports/budget-vs-actual", _ => new HttpResponseMessage(System.Net.HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"status\":422,\"code\":\"budget.noneApproved\",\"detail\":\"No approved budget for 2026.\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        });
        var cut = Render<BudgetVsActual>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("No approved budget for 2026.", Text(cut)));
        Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains(L["nav.budgets"], StringComparison.Ordinal));
    }
}

/// <summary>A user who may only view budgets: the budget page has no editing, no approval.</summary>
public sealed class BudgetViewerPageTests() : PageTestBase(PermissionKeys.BudgetView)
{
    [Fact]
    public void Viewer_sees_the_draft_without_editing_or_approving()
    {
        BudgetPageTests.Options(this, Respond);
        Respond("accounting/budgets/b1", BudgetPageTests.Budget());
        var cut = Render<BudgetEditor>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "b1"));
        cut.WaitForAssertion(() => Assert.Contains(L["bud.lines"], cut.Markup));
        Assert.Empty(cut.FindAll("tbody input"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains(L["bud.approve"], StringComparison.Ordinal) || b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal));
    }
}
