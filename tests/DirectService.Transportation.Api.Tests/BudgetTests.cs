using System.Net;
using System.Net.Http.Json;
using System.Text;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Security;
using static DirectService.Transportation.Api.Tests.PartBSupport;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Revision 6 part B, feature 1 (owner change request 15 Oct 2026): budgets — save, rules (account types, duplicates, parent/sub-account
/// overlap), approval (one per year, read-only while approved), copy from last year's actuals with an increase, Excel import (template,
/// strict preview, replace), Budget vs Actual (month / year to date, tree, cost centre, variance signs, export), history and permissions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BudgetTests(ApiFactory f)
{
    private static decimal[] Month(int month, decimal amount)
    {
        var m = new decimal[12];
        m[month - 1] = amount;
        return m;
    }

    private static BudgetLineRequest Line(AccountResponse a, decimal[] months, string? centre = null) => new(a.Id, centre, months);

    private static async Task<BudgetResponse> CreateAsync(SignedInUser o, string name, int? year = null, bool copy = false, decimal? percent = null, bool byCentre = false) =>
        await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/budgets"), new CreateBudgetRequest(year ?? Today.Year, name, copy, percent, byCentre)));

    private static Task<HttpResponseMessage> SaveAsync(SignedInUser o, BudgetResponse b, params BudgetLineRequest[] lines) =>
        o.Client.PutAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}"), new BudgetRequest(b.Name, lines, b.RowVersion));

    [Fact]
    public async Task Budget_vs_actual_by_month_and_year_to_date_with_tree_totals_and_variance_signs()
    {
        var (o, a) = await CompanyAsync(f);
        var month = Today.Month;
        await PostAsync(o, Today, a["110101"], a["4102"], 1200);   // revenue 1,200 against a budget of 1,000 → favourable
        await PostAsync(o, Today, a["5202"], a["110101"], 300);    // 52 (parent budget 400): 300 + 50 = 350 → favourable
        await PostAsync(o, Today, a["5203"], a["110101"], 50);
        await PostAsync(o, Today, a["5104"], a["110101"], 30);     // expense 30 against 10 → unfavourable

        var b = await CreateAsync(o, "Plan");
        Assert.Equal(BudgetStatus.Draft, b.Status);
        b = await OkAsync<BudgetResponse>(await SaveAsync(o, b, Line(a["4102"], Month(month, 1000)), Line(a["52"], Month(month, 400)), Line(a["5104"], Month(month, 10)),
            Line(a["5101"], Month(month, 100))));
        Assert.Equal(4, b.Lines.Count);
        Assert.Equal(1000, b.RevenueTotal);
        Assert.Equal(510, b.ExpenseTotal);
        Assert.True(b.Lines.Single(l => l.Account.Code == "52").IsParent);

        // Only an approved budget is compared.
        var none = await o.Client.GetAsync(U(o, "/accounting/reports/budget-vs-actual"));
        Assert.Equal("budget.noneApproved", await none.ErrorCodeAsync());

        b = await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(b.RowVersion)));
        Assert.Equal(BudgetStatus.Approved, b.Status);
        Assert.NotNull(b.ApprovedAt);

        var r = (await o.Client.GetFromJsonAsync<BudgetVsActualReport>(U(o, $"/accounting/reports/budget-vs-actual?year={Today.Year}&month={month}&mode=month")))!;
        Row("4102", 1000, 1200, 200, 20.0m, "favourable");
        Row("4", 1000, 1200, 200, 20.0m, "favourable");             // parent = sum of its children
        Row("52", 400, 350, -50, -12.5m, "favourable");             // budget on the parent, actuals on its sub-accounts
        Row("5202", 0, 300, 300, null, "unfavourable");             // no budget of its own: no %
        Row("5104", 10, 30, 20, 200.0m, "unfavourable");
        Row("5101", 100, 0, -100, -100.0m, "favourable");
        Row("51", 110, 30, -80, -72.7m, "favourable");
        Row("5", 510, 380, -130, -25.5m, "favourable");
        Assert.True(r.Rows.Single(x => x.Account.Code == "52").IsGroup);
        Assert.Equal(r.Rows.Single(x => x.Account.Code == "52").Account.Id, r.Rows.Single(x => x.Account.Code == "5202").ParentId);
        Assert.True(r.Rows.ToList().FindIndex(x => x.Account.Code == "4") < r.Rows.ToList().FindIndex(x => x.Account.Code == "5"));   // revenue, then expenses
        Assert.Equal((1000m, 1200m, "favourable"), (r.Revenue.Budget, r.Revenue.Actual, r.Revenue.Status));
        Assert.Equal((510m, 380m, "favourable"), (r.Expenses.Budget, r.Expenses.Actual, r.Expenses.Status));
        Assert.Equal((490m, 820m, 330m), (r.Net.Budget, r.Net.Actual, r.Net.Variance));

        // Year to date covers January … the month; another month of the year has a budget but no actuals.
        if (month > 1)
        {
            var other = await o.Client.GetFromJsonAsync<BudgetVsActualReport>(U(o, $"/accounting/reports/budget-vs-actual?year={Today.Year}&month={month - 1}&mode=month"));
            Assert.Equal(0, other!.Revenue.Actual);
            Assert.Equal(0, other.Revenue.Budget);
        }
        var ytd = await o.Client.GetFromJsonAsync<BudgetVsActualReport>(U(o, $"/accounting/reports/budget-vs-actual?year={Today.Year}&month={month}"));
        Assert.Equal(("ytd", new DateOnly(Today.Year, 1, 1)), (ytd!.Mode, ytd.From));
        Assert.Equal(1200, ytd.Revenue.Actual);

        // Export: Excel and PDF printout.
        var xlsx = await o.Client.GetAsync(U(o, $"/accounting/reports/budget-vs-actual/export?year={Today.Year}&month={month}&mode=month&format=xlsx"));
        await xlsx.EnsureOkAsync();
        Assert.Contains("spreadsheetml", xlsx.Content.Headers.ContentType!.MediaType, StringComparison.Ordinal);
        var html = Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o, $"/accounting/reports/budget-vs-actual/export?year={Today.Year}&month={month}&mode=month&format=html"))).Content.ReadAsByteArrayAsync());
        Assert.Contains("Budget vs Actual", html, StringComparison.Ordinal);
        Assert.Contains("Unfavourable", html, StringComparison.Ordinal);

        void Row(string code, decimal budget, decimal actual, decimal variance, decimal? pct, string status)
        {
            var x = r!.Rows.Single(x => x.Account.Code == code);
            Assert.Equal((budget, actual, variance, pct, status), (x.Budget, x.Actual, x.Variance, x.VariancePercent, x.Status));
        }
    }

    [Fact]
    public async Task Lines_are_checked_and_an_approved_budget_is_read_only_and_unique_per_year()
    {
        var (o, a) = await CompanyAsync(f);
        var b = await CreateAsync(o, "Main");
        var twelve = Month(1, 5);

        Assert.Equal("budget.overlap", await (await SaveAsync(o, b, Line(a["52"], twelve), Line(a["5202"], twelve))).ErrorCodeAsync());
        Assert.Equal("budget.duplicateLine", await (await SaveAsync(o, b, Line(a["5202"], twelve), Line(a["5202"], twelve))).ErrorCodeAsync());
        Assert.Equal("validation.range", await (await SaveAsync(o, b, new BudgetLineRequest(a["5202"].Id, null, [1, 2]))).ErrorCodeAsync());
        Assert.Equal("validation.range", await (await SaveAsync(o, b, Line(a["5202"], Month(2, 1.2345m)))).ErrorCodeAsync());
        Assert.Equal("budget.nameTaken", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/budgets"), new CreateBudgetRequest(Today.Year, "Main"))).ErrorCodeAsync());

        // The same account twice with different cost centres is fine; a negative amount (e.g. a contra account) too.
        var cc = await OkAsync<CostCentreResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/cost-centres"), new CostCentreRequest("CC1", "North", null)));
        b = await OkAsync<BudgetResponse>(await SaveAsync(o, b, Line(a["5202"], twelve), Line(a["5202"], Month(3, 7), cc.Id), Line(a["4104"], Month(1, -20))));
        Assert.Equal(3, b.Lines.Count);

        // Approving needs lines; one approved budget per year; approved = read-only and not deletable.
        var empty = await CreateAsync(o, "Empty");
        Assert.Equal("budget.empty", await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{empty.Id}/approve"), new RowVersionRequest(empty.RowVersion))).ErrorCodeAsync());
        b = await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(b.RowVersion)));
        Assert.Equal("budget.approved", await (await SaveAsync(o, b, Line(a["5202"], twelve))).ErrorCodeAsync());
        Assert.Equal("budget.approved", await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/delete"), new RowVersionRequest(b.RowVersion))).ErrorCodeAsync());
        var second = await CreateAsync(o, "Second");
        second = await OkAsync<BudgetResponse>(await SaveAsync(o, second, Line(a["5202"], twelve)));
        var refused = await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{second.Id}/approve"), new RowVersionRequest(second.RowVersion));
        Assert.Equal("budget.approvedExists", await refused.ErrorCodeAsync());
        Assert.Contains("Main", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Return to draft, then the other one can be approved; drafts can be deleted.
        b = await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/return-to-draft"), new RowVersionRequest(b.RowVersion)));
        Assert.Equal((BudgetStatus.Draft, (DateTime?)null), (b.Status, b.ApprovedAt));
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{second.Id}/approve"), new RowVersionRequest(second.RowVersion))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/delete"), new RowVersionRequest(b.RowVersion))).StatusCode);
        var list = (await o.Client.GetFromJsonAsync<List<BudgetSummary>>(U(o, $"/accounting/budgets?year={Today.Year}")))!;
        Assert.Equal(["Empty", "Second"], list!.Select(x => x.Name).Order());
        Assert.Equal(BudgetStatus.Approved, list.Single(x => x.Name == "Second").Status);

        // A stale row version is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await SaveAsync(o, empty with { RowVersion = second.RowVersion }, Line(a["5202"], twelve))).StatusCode);
    }

    [Fact]
    public async Task Copy_from_last_year_actuals_with_an_increase_and_by_cost_centre()
    {
        var (o, a) = await CompanyAsync(f);
        var last = Today.Year - 1;
        Assert.Equal("budget.noActuals", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/budgets"), new CreateBudgetRequest(Today.Year, "Nothing", true, 10))).ErrorCodeAsync());

        var cc = await OkAsync<CostCentreResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/cost-centres"), new CostCentreRequest("CC1", "North", null)));
        await PostAsync(o, new DateOnly(last, 3, 10), a["110101"], a["4101"], 1000);
        await PostAsync(o, new DateOnly(last, 3, 20), a["110101"], a["4101"], 500, cc.Id);
        await PostAsync(o, new DateOnly(last, 7, 1), a["5202"], a["110101"], 333.335m);
        await PostAsync(o, new DateOnly(last, 7, 2), a["110101"], a["5202"], 0.005m);   // nets to 333.330
        await PostAsync(o, new DateOnly(Today.Year, 1, 1), a["5203"], a["110101"], 99); // this year: not copied

        var b = await CreateAsync(o, "From actuals", copy: true, percent: 10);
        Assert.Equal(2, b.Lines.Count);
        Assert.Equal(1650m, b.Lines.Single(l => l.Account.Code == "4101").Months[2]);          // (1000 + 500) × 1.1
        Assert.Equal(366.663m, b.Lines.Single(l => l.Account.Code == "5202").Months[6]);       // 333.330 × 1.1, rounded to 3 decimals
        Assert.Equal(366.663m, b.Lines.Single(l => l.Account.Code == "5202").Total);
        Assert.Null(b.Lines.Single(l => l.Account.Code == "4101").CostCentre);

        var byCentre = await CreateAsync(o, "By centre", copy: true, byCentre: true);
        Assert.Equal(3, byCentre.Lines.Count);
        Assert.Equal(1000m, byCentre.Lines.Single(l => l.Account.Code == "4101" && l.CostCentre is null).Months[2]);
        Assert.Equal(500m, byCentre.Lines.Single(l => l.Account.Code == "4101" && l.CostCentre?.Id == cc.Id).Months[2]);

        var history = await o.Client.GetFromJsonAsync<List<HistoryEntry>>(U(o, $"/accounting/budgets/{b.Id}/history"));
        Assert.Contains(history!, h => h.Action == "Create" && h.Reason is { } reason && reason.Contains($"{last}", StringComparison.Ordinal));
        Assert.Contains(history!, h => h.Text is { } t && t.Contains("4101", StringComparison.Ordinal));    // a line event names its account
    }

    [Fact]
    public async Task Excel_import_template_strict_preview_and_replace()
    {
        var (o, a) = await CompanyAsync(f);
        var b = await CreateAsync(o, "Imported");
        b = await OkAsync<BudgetResponse>(await SaveAsync(o, b, Line(a["5205"], Month(1, 1))));

        // The template imports as it is (sample rows on the company's first revenue and expense posting accounts).
        var template = await (await o.Client.GetAsync(U(o, $"/accounting/budgets/{b.Id}/import/template"))).Content.ReadAsByteArrayAsync();
        var preview = await Upload<ImportPreview>("import/preview", template, "budget.xlsx");
        Assert.Equal((2, 0), (preview.ValidRows, preview.RejectedRows));
        var result = await Upload<ImportResult>("import", template, "budget.xlsx");
        Assert.Equal(2, result.ImportedRows);
        var after = await o.Client.GetFromJsonAsync<BudgetResponse>(U(o, $"/accounting/budgets/{b.Id}"));
        Assert.Equal(["4101", "5101"], after!.Lines.Select(l => l.Account.Code));   // 5205 replaced
        Assert.Equal(120000m, after.Lines.Single(l => l.Account.Code == "4101").Total);

        // Problems with row, column and message; nothing imported while any row is rejected. Arabic month names and negatives are read.
        var csv = """
            رمز الحساب,مركز التكلفة,كانون الثاني,شباط,March
            4102,,100,(5),1
            9999,,1,,
            110101,,1,,
            5202,NOPE,1,,
            5203,,abc,,
            52,,1,,
            4102,,1,,
            5201,,1,,
            """;
        preview = await Upload<ImportPreview>("import/preview", Encoding.UTF8.GetBytes(csv), "b.csv");
        Assert.Equal(3, preview.ValidRows);   // 4102, 110101 (balance-sheet accounts may be budgeted since DT-265) and 52; 5201 overlaps 52
        var problems = preview.Rows.Where(r => r.Problems.Count > 0).ToDictionary(r => r.Row, r => r.Details!.Select(d => d.Code).ToList());
        Assert.Equal("import.unknown", problems[3][0]);
        Assert.False(problems.ContainsKey(4));
        Assert.Equal("import.unknown", problems[5][0]);
        Assert.Equal("import.invalid", problems[6][0]);
        Assert.Equal("import.duplicate", problems[8][0]);
        Assert.Equal("budget.overlap", problems[9][0]);
        var refused = await o.Client.PostAsync(U(o, $"/accounting/budgets/{b.Id}/import"),
            new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "b.csv" } });
        Assert.Equal("import.hasErrors", await refused.ErrorCodeAsync());
        var good = "Account code,January,February\n4102,100,-5\n";
        await Upload<ImportResult>("import", Encoding.UTF8.GetBytes(good), "g.csv");
        var line = (await o.Client.GetFromJsonAsync<BudgetResponse>(U(o, $"/accounting/budgets/{b.Id}")))!.Lines.Single();
        Assert.Equal((100m, -5m, 95m), (line.Months[0], line.Months[1], line.Total));

        // An approved budget cannot be imported into.
        var current = (await o.Client.GetFromJsonAsync<BudgetResponse>(U(o, $"/accounting/budgets/{b.Id}")))!;
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(current.RowVersion))).EnsureOkAsync();
        var approved = await o.Client.PostAsync(U(o, $"/accounting/budgets/{b.Id}/import"),
            new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(good)), "file", "g.csv" } });
        Assert.Equal("budget.approved", await approved.ErrorCodeAsync());

        async Task<T> Upload<T>(string path, byte[] bytes, string name) =>
            await OkAsync<T>(await o.Client.PostAsync(U(o, $"/accounting/budgets/{b.Id}/{path}"), new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", name } }));
    }

    [Fact]
    public async Task Budget_permissions_view_manage_approve()
    {
        var (o, a) = await CompanyAsync(f);
        var b = await CreateAsync(o, "Perm");
        b = await OkAsync<BudgetResponse>(await SaveAsync(o, b, Line(a["5202"], Month(1, 10))));

        var viewer = await MemberAsync(f, o, PermissionKeys.BudgetView);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(U(o, "/accounting/budgets"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(U(o, $"/accounting/budgets/{b.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(U(o, "/accounting/budgets/account-options"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(U(o, "/accounting/cost-centres/options"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(U(o, "/accounting/budgets"), new CreateBudgetRequest(Today.Year, "X"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}"), new BudgetRequest("X", [], b.RowVersion))).StatusCode);

        var manager = await MemberAsync(f, o, PermissionKeys.BudgetView, PermissionKeys.BudgetManage);
        b = await OkAsync<BudgetResponse>(await manager.PutAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}"), new BudgetRequest("Perm 2", [Line(a["5202"], Month(1, 20))], b.RowVersion)));
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(b.RowVersion))).StatusCode);

        var approver = await MemberAsync(f, o, PermissionKeys.BudgetView, PermissionKeys.BudgetApprove);
        await (await approver.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(b.RowVersion))).EnsureOkAsync();

        var none = await MemberAsync(f, o, PermissionKeys.AcctView);
        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync(U(o, "/accounting/budgets"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await none.GetAsync(U(o, "/accounting/reports/budget-vs-actual"))).StatusCode);

        // Preset roles: Accountant view + manage, Viewer view, Owner / Administrator all three.
        Assert.Contains(PermissionKeys.BudgetManage, PresetRoles.Permissions[PresetRoles.Accountant]);
        Assert.DoesNotContain(PermissionKeys.BudgetApprove, PresetRoles.Permissions[PresetRoles.Accountant]);
        Assert.Equal([PermissionKeys.BudgetView], PresetRoles.Permissions[PresetRoles.Viewer].Where(k => k.StartsWith("acc.budget", StringComparison.Ordinal)));
        Assert.Contains(PermissionKeys.BudgetApprove, PresetRoles.Permissions[PresetRoles.Administrator]);
    }
}
