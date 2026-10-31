using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;
using static DirectService.Transportation.Api.Tests.PartBSupport;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Revision 6 part B, feature 3 (owner change request 15 Oct 2026): the accounting dashboard — key figures of a month and the year to date,
/// budget usage, 12-month trend, receivables ageing, every reminder rule with its count and link, and permissions (each part only with the
/// permission that opens it).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountingDashboardTests(ApiFactory f)
{
    private static string Q(DateOnly d) => $"?year={d.Year}&month={d.Month}";

    private static async Task<AccountingDashboardResponse> DashboardAsync(HttpClient c, SignedInUser o, DateOnly? month = null) =>
        (await c.GetFromJsonAsync<AccountingDashboardResponse>(U(o, "/accounting/dashboard" + Q(month ?? Today))))!;

    private Task<(long Tenant, long Company)> IdsAsync(string description) =>
        f.DbAsync(db => db.Set<JournalEntry>().IgnoreQueryFilters().Where(e => e.Description == description).Select(e => new ValueTuple<long, long>(e.TenantId, e.CompanyId)).FirstAsync());

    [Fact]
    public async Task Figures_trend_budget_usage_and_ageing_of_the_month()
    {
        var (o, a) = await CompanyAsync(f);
        var marker = "Dashboard " + Guid.NewGuid().ToString("N")[..8];
        await PostAsync(o, Today, a["110101"], a["4102"], 1000, description: marker);    // revenue 1,000 in cash
        await PostAsync(o, Today, a["5202"], a["110101"], 300);                         // expense 300 in cash

        var d = await DashboardAsync(o.Client, o);
        Assert.Equal((Today.Year, Today.Month, new DateOnly(Today.Year, 1, 1)), (d.Year, d.Month, d.YearFrom));
        var cashBanks = (await o.Client.GetFromJsonAsync<List<CashBankBalance>>(U(o, $"/accounting/reports/cash-bank-balances?asOf={D(d.MonthTo)}")))!;
        Assert.Equal(cashBanks.Where(b => b.Status == "Active").Sum(b => b.Balance), d.Figures!.CashAndBank);
        Assert.Equal(700m, d.Figures.CashAndBank);
        Assert.Equal((1000m, 300m, 700m), (d.Figures.Month!.Revenue, d.Figures.Month.Expenses, d.Figures.Month.NetProfit));
        Assert.Equal((1000m, 300m, 700m), (d.Figures.YearToDate!.Revenue, d.Figures.YearToDate.Expenses, d.Figures.YearToDate.NetProfit));
        Assert.Equal(12, d.Trend!.Count);
        Assert.Equal((AccountingCodes(Today), 1000m, 300m), (d.Trend[^1].Month, d.Trend[^1].Revenue, d.Trend[^1].Expenses));
        Assert.All(d.Trend.SkipLast(1), t => Assert.Equal((0m, 0m), (t.Revenue, t.Expenses)));
        Assert.Null(d.Budget);   // no approved budget yet

        // Receivables (a receivable document of 2,000 + 16 % tax, due 35 days ago) and payables (a supplier bill).
        var customer = await OkAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Customer", "Ports Company", "500600700", null, null, a["110302"].Id, 0, null, null)));
        await OkAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/receivables"),
            new ApArDocumentRequest(customer.Id, null, Today.AddDays(-40), Today.AddDays(-35), null, [new ApArLineRequest("Crane rental", a["4102"].Id, 2000m, 16m, null)], null)));
        var supplier = await OkAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Supplier", "Fuel Co", "100200300", null, null, a["2101"].Id, 30, null, null)));
        await OkAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/bills"),
            new ApArDocumentRequest(supplier.Id, null, Today, null, null, [new ApArLineRequest("Diesel", a["5103"].Id, 500m, 0m, null)], null)));

        // An approved budget: expenses 200 for the year, revenue 4,000.
        var b = await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/budgets"), new CreateBudgetRequest(Today.Year, "Plan")));
        var m = new decimal[12];
        m[Today.Month - 1] = 200;
        var r = new decimal[12];
        r[Today.Month - 1] = 4000;
        b = await OkAsync<BudgetResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}"),
            new BudgetRequest("Plan", [new BudgetLineRequest(a["5202"].Id, null, m), new BudgetLineRequest(a["4102"].Id, null, r)], b.RowVersion)));
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(b.RowVersion))).EnsureOkAsync();

        d = await DashboardAsync(o.Client, o);
        Assert.Equal(2320m, d.Figures!.Receivables);
        Assert.Equal(2320m, d.ReceivablesAgeing!.Days31To60);
        Assert.Equal((500m, 0m, 0m, 500m), (d.Figures.Payables!.Suppliers, d.Figures.Payables.Drivers, d.Figures.Payables.Partners, d.Figures.Payables.Total));
        Assert.Equal((200m, 300m + 500m, 400.0m), (d.Budget!.ExpenseBudget, d.Budget.ExpenseActual, d.Budget.ExpenseUsedPercent));   // 800 of 200
        Assert.Equal(4000m, d.Budget.RevenueBudget);
        Assert.Equal("Plan", d.Budget.BudgetName);
        Assert.InRange(d.Budget.YearElapsedPercent, 0, 100);

        // An earlier month shows that month only (as-of date at its end): the receivable dated 40 days ago is its revenue if it falls in it.
        if (Today.Month > 1)
        {
            var earlier = await DashboardAsync(o.Client, o, Today.AddMonths(-1));
            var previousMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
            Assert.Equal(new DateOnly(Today.Year, Today.Month, 1).AddDays(-1), earlier.MonthTo);
            var dated = Today.AddDays(-40);
            Assert.Equal(dated.Year == previousMonth.Year && dated.Month == previousMonth.Month ? 2000m : 0m, earlier.Figures!.Month!.Revenue);
            Assert.Equal(0m, earlier.Figures.Month.Expenses);
        }
        Assert.Equal("validation.range", await (await o.Client.GetAsync(U(o, "/accounting/dashboard?year=2026&month=13"))).ErrorCodeAsync());
        _ = await IdsAsync(marker);
    }

    private static string AccountingCodes(DateOnly d) => d.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Every_reminder_is_counted_with_its_link()
    {
        var (o, a) = await CompanyAsync(f);
        var d0 = await DashboardAsync(o.Client, o);
        Assert.Empty(d0.Reminders);   // a new company: nothing to remind

        // Draft journal entry.
        await OkAsync<JournalDraftResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-drafts"),
            new ManualEntryRequest(Today, "Draft", [new JournalLineRequest(a["5202"].Id, 10, 0, null, null, null)], null)));
        // Previous months still open: an entry two months ago (this month and later are not "previous").
        var marker = "Old " + Guid.NewGuid().ToString("N")[..8];
        var old = Today.AddMonths(-2);
        await PostAsync(o, old, a["5202"], a["110101"], 50, description: marker);
        await PostAsync(o, Today, a["5202"], a["110101"], 30);
        // Overdue receivable document; supplier bill due within 7 days; one due later (not counted).
        var customer = await OkAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Customer", "Ports Company", "500600700", null, null, a["110302"].Id, 0, null, null)));
        var receivable = await OkAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/receivables"),
            new ApArDocumentRequest(customer.Id, null, Today.AddDays(-20), Today.AddDays(-5), null, [new ApArLineRequest("Crane", a["4102"].Id, 100m, 0m, null)], null)));
        var supplier = await OkAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Supplier", "Fuel Co", "100200300", null, null, a["2101"].Id, 30, null, null)));
        await OkAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/bills"),
            new ApArDocumentRequest(supplier.Id, null, Today, Today.AddDays(3), null, [new ApArLineRequest("Diesel", a["5103"].Id, 70m, 0m, null)], null)));
        await OkAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/bills"),
            new ApArDocumentRequest(supplier.Id, null, Today, Today.AddDays(20), null, [new ApArLineRequest("Oil", a["5104"].Id, 30m, 0m, null)], null)));
        // Receipt on account (not allocated).
        var transport = await OkAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"),
            new CustomerRequest("Arab Potash", null, null, "0781234567", null, null, null, null, 30, null, null, null, null, null)));
        var box = (await o.Client.GetFromJsonAsync<List<CashBoxResponse>>(U(o, "/accounting/cash-boxes")))!.First();
        await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), new ReceiptRequest(transport.Id, Today, null, 250m, "Cash", box.Id, null, null, 0, null, null))).EnsureOkAsync();
        // Budget line over 100%: expense 5202 budget 20 this month, actual 30 this month (+ 50 two months ago when in this year) > 20.
        var b = await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/budgets"), new CreateBudgetRequest(Today.Year, "Plan")));
        var months = new decimal[12];
        months[Today.Month - 1] = 20;
        b = await OkAsync<BudgetResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}"), new BudgetRequest("Plan", [new BudgetLineRequest(a["5202"].Id, null, months)], b.RowVersion)));
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(b.RowVersion))).EnsureOkAsync();
        // Pending posting and a rejected e-invoice (written directly: they come from failures of outside services).
        var (tenant, company) = await IdsAsync(marker);
        await f.DbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("INSERT INTO [acc].[PendingPostings] ([TenantId], [CompanyId], [SourceModule], [SourceType], [SourceId], [Reason], [Attempts]) VALUES ({0}, {1}, 'ACC', 'Manual', 0, 'test', 1)",
                tenant, company);
            var docId = await db.Set<ApArDocument>().IgnoreQueryFilters().Where(x => x.CompanyId == company && x.DocumentKind == ApArCodes.Receivable).Select(x => x.Id).SingleAsync();
            var updated = await db.Database.ExecuteSqlRawAsync("UPDATE [acc].[ApArEInvoiceSubmissions] SET [Status] = 'Rejected' WHERE [ApArDocumentId] = {0}", docId);
            if (updated == 0)
                await db.Database.ExecuteSqlRawAsync("INSERT INTO [acc].[ApArEInvoiceSubmissions] ([TenantId], [CompanyId], [DocumentType], [ApArDocumentId], [Status], [Attempts]) VALUES ({0}, {1}, 'Invoice', {2}, 'Rejected', 1)",
                    tenant, company, docId);
            return 0;
        });
        // "Supplier deductions" unmapped: no reminder until a bill with a deduction is entered (refused) — then it shows.
        await f.DbAsync(db => db.Database.ExecuteSqlRawAsync("DELETE FROM [acc].[PostingKeyMappings] WHERE [CompanyId] = {0} AND [PostingKey] = 'ACC.SupplierDeductions'", company));
        Assert.DoesNotContain((await DashboardAsync(o.Client, o)).Reminders, x => x.Key == AccountingReminderKeys.DeductionsUnmapped);
        var refused = await o.Client.PostAsJsonAsync(U(o, "/apar/bills"),
            new ApArDocumentRequest(supplier.Id, null, Today, null, null, [new ApArLineRequest("Diesel", a["5103"].Id, 100m, 0m, null, 5m)], null));
        Assert.Equal("apar.deductionKeyMissing", await refused.ErrorCodeAsync());

        var d = await DashboardAsync(o.Client, o);
        var byKey = d.Reminders.ToDictionary(x => x.Key);
        Assert.Equal(AccountingReminderKeys.All.Order(), byKey.Keys.Order());
        Assert.Equal((1, "accounting/journal?view=Draft"), (byKey[AccountingReminderKeys.DraftJournals].Count, byKey[AccountingReminderKeys.DraftJournals].Route));
        Assert.Equal((1, "accounting/automatic?tab=pending"), (byKey[AccountingReminderKeys.PendingPostings].Count, byKey[AccountingReminderKeys.PendingPostings].Route));
        Assert.Equal(2, byKey[AccountingReminderKeys.OpenMonths].Count);   // two months ago and last month
        Assert.Equal((1, 100m), (byKey[AccountingReminderKeys.OverdueInvoices].Count, byKey[AccountingReminderKeys.OverdueInvoices].Amount));
        Assert.Equal((1, 70m, "accounting/payables?view=due-soon"), (byKey[AccountingReminderKeys.BillsDueSoon].Count, byKey[AccountingReminderKeys.BillsDueSoon].Amount, byKey[AccountingReminderKeys.BillsDueSoon].Route));
        Assert.Equal(1, byKey[AccountingReminderKeys.RejectedEInvoices].Count);
        Assert.Equal((1, $"accounting/budget-vs-actual?year={Today.Year}"), (byKey[AccountingReminderKeys.BudgetOver].Count, byKey[AccountingReminderKeys.BudgetOver].Route));
        Assert.Equal((1, 250m, "receipts?view=unallocated"), (byKey[AccountingReminderKeys.UnallocatedReceipts].Count, byKey[AccountingReminderKeys.UnallocatedReceipts].Amount, byKey[AccountingReminderKeys.UnallocatedReceipts].Route));
        Assert.Equal((1, "settings/mapping"), (byKey[AccountingReminderKeys.DeductionsUnmapped].Count, byKey[AccountingReminderKeys.DeductionsUnmapped].Route));

        // The lists the links open filter the same records.
        Assert.Single((await o.Client.GetFromJsonAsync<System.Text.Json.JsonElement>(U(o, "/apar/bills?payment=due-soon"))).GetProperty("page").GetProperty("items").EnumerateArray());
        Assert.Single((await o.Client.GetFromJsonAsync<System.Text.Json.JsonElement>(U(o, "/receipts?applied=unallocated"))).GetProperty("page").GetProperty("items").EnumerateArray());

        // Mapping the key and closing nothing else: the deductions reminder goes away.
        await (await o.Client.PutAsJsonAsync(U(o, "/accounting/posting-keys/ACC.SupplierDeductions"), new PostingKeyRequest(a["4105"].Id))).EnsureOkAsync();
        Assert.DoesNotContain((await DashboardAsync(o.Client, o)).Reminders, x => x.Key == AccountingReminderKeys.DeductionsUnmapped);
    }

    /// <summary>Owner decision 18 Oct 2026: what is owed to suppliers, drivers and partners, as their account pages show it, with the total.</summary>
    [Fact]
    public async Task Owed_to_suppliers_drivers_and_partners_with_the_total_by_permission()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var a = (await o.Client.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(x => x.Code);
        await w.Trip("OWE-1", w.Driver, null);                    // own driver: a wage owed
        await w.Trip("OWE-2", w.PartnerDriver, w.Partner.Id);     // partner's truck: a partner cost owed
        await (await o.Client.PostAsJsonAsync(U(o, "/advances"), new { driverId = w.Driver.Id, advanceDate = D(Today.AddDays(-1)), amount = 5m, paymentMethod = "Cash", cashBoxId = w.CashBox }))
            .EnsureOkAsync();
        var supplier = await OkAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Supplier", "Fuel Co", "100200300", null, null, a["2101"].Id, 30, null, null)));
        await OkAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/bills"),
            new ApArDocumentRequest(supplier.Id, null, Today.AddDays(-1), null, null, [new ApArLineRequest("Diesel", a["5103"].Id, 400m, 0m, null)], null)));

        // DT-278: the "owed" column (what the company must pay); the last column is what they owe the company.
        decimal Total(DirectService.Shared.Contracts.ReportResult r) => ((System.Text.Json.JsonElement)r.Totals![^2]!).GetDecimal();
        var drivers = Total((await o.Client.GetFromJsonAsync<DirectService.Shared.Contracts.ReportResult>(U(o, $"/reports/standard/driver-balances?asOf={D(Today)}")))!);
        var partners = Total((await o.Client.GetFromJsonAsync<DirectService.Shared.Contracts.ReportResult>(U(o, $"/reports/standard/partner-balances?asOf={D(Today)}")))!);
        Assert.True(drivers > 0 && partners > 0);

        var p = (await DashboardAsync(o.Client, o)).Figures!.Payables!;
        Assert.Equal((400m, drivers, partners, 400m + drivers + partners), (p.Suppliers, p.Drivers, p.Partners, p.Total));

        // Driver-accounts rights only: drivers and partners, not suppliers; accounting rights only: suppliers, not drivers and partners.
        var driverDesk = await MemberAsync(f, o, PermissionKeys.DrvView, PermissionKeys.JeView);
        var pd = (await DashboardAsync(driverDesk, o)).Figures!.Payables!;
        Assert.Equal(((decimal?)null, drivers, partners, drivers + partners), (pd.Suppliers, pd.Drivers, pd.Partners, pd.Total));
        var accountant = await MemberAsync(f, o, PermissionKeys.AcctView);
        var pa = (await DashboardAsync(accountant, o)).Figures!.Payables!;
        Assert.Equal((400m, (decimal?)null, (decimal?)null, 400m), (pa.Suppliers, pa.Drivers, pa.Partners, pa.Total));
    }

    [Fact]
    public async Task Each_part_needs_the_permission_that_opens_it()
    {
        var (o, a) = await CompanyAsync(f);
        await OkAsync<JournalDraftResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-drafts"),
            new ManualEntryRequest(Today, "Draft", [new JournalLineRequest(a["5202"].Id, 10, 0, null, null, null)], null)));
        var b = await OkAsync<BudgetResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/budgets"), new CreateBudgetRequest(Today.Year, "Plan")));
        b = await OkAsync<BudgetResponse>(await o.Client.PutAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}"), new BudgetRequest("Plan", [new BudgetLineRequest(a["5202"].Id, null, [1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])], b.RowVersion)));   // an all-zero line is not kept (DT-265)
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/budgets/{b.Id}/approve"), new RowVersionRequest(b.RowVersion))).EnsureOkAsync();

        // Budget viewer only: budget usage, no ledger figures, no journal reminders.
        var budgetOnly = await MemberAsync(f, o, PermissionKeys.BudgetView);
        var d = await DashboardAsync(budgetOnly, o);
        Assert.Null(d.Figures);
        Assert.Null(d.Trend);
        Assert.Null(d.ReceivablesAgeing);
        Assert.NotNull(d.Budget);
        Assert.DoesNotContain(d.Reminders, x => x.Key == AccountingReminderKeys.DraftJournals);

        // Journal poster: the draft reminder, no figures, no budget.
        var poster = await MemberAsync(f, o, PermissionKeys.AcctPost);
        d = await DashboardAsync(poster, o);
        Assert.Contains(d.Reminders, x => x.Key == AccountingReminderKeys.DraftJournals);
        Assert.Null(d.Figures);
        Assert.Null(d.Budget);

        // Accounting viewer: ledger figures and trend, no budget.
        var viewer = await MemberAsync(f, o, PermissionKeys.AcctView);
        d = await DashboardAsync(viewer, o);
        Assert.NotNull(d.Figures!.Month);
        Assert.NotNull(d.Figures.CashAndBank);
        Assert.Null(d.Budget);

        // No accounting permission at all: refused; another company's dashboard: refused.
        var dispatcher = await MemberAsync(f, o, PermissionKeys.TripsView);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.GetAsync(U(o, "/accounting/dashboard"))).StatusCode);
        var (other, _) = await CompanyAsync(f);
        Assert.NotEqual(HttpStatusCode.OK, (await o.Client.GetAsync(U(other, "/accounting/dashboard"))).StatusCode);
    }
}
