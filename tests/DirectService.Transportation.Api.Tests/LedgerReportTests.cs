using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;

using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B9 financial reports (FR-116 … FR-121, FR-130, FR-131, FR-105) on a small known ledger, and the maintenance zone (FR-135, FR-136).
/// Ledger: opening 110101 Dr 1,000 + 110301 Dr 300 / 3101 Cr 1,300; manual 5203 Dr 50 (cost centre) / 110101 Cr 50; supplier bill 5103 100
/// + tax 16 / 2101 Cr 116; receivable 110302 Dr 232 / 4102 Cr 200 + tax 32; collection 110101 Dr 232 / 110302 Cr 232.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LedgerReportTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);
    private static readonly DateOnly Start = Today.AddDays(-10);

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static JournalLineRequest Dr(AccountResponse a, decimal amount, string? cc = null) => new(a.Id, amount, 0, cc, null, null);

    private static JournalLineRequest Cr(AccountResponse a, decimal amount) => new(a.Id, 0, amount, null, null, null);

    private async Task<ApArTests.World> LedgerAsync()
    {
        var w = await ApArTests.WorldAsync(f);
        var o = w.Owner;
        var a = w.Accounts;
        await (await o.Client.PutAsJsonAsync(U(o, "/accounting/opening-balances"), new OpeningBalancesRequest([
            new OpeningBalanceLine(a["110101"].Id, 1000m, 0), new OpeningBalanceLine(a["110301"].Id, 300m, 0), new OpeningBalanceLine(a["3101"].Id, 0, 1300m)]))).EnsureOkAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/opening-balances/post"), new PostOpeningRequest(Start))).EnsureOkAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), new ManualEntryRequest(Today, "Internet October", [Dr(a["5203"], 50m, w.CostCentre), Cr(a["110101"], 50m)], null)))
            .EnsureOkAsync();
        var supplier = await ApArTests.SupplierAsync(w);
        await (await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("Diesel", a["5103"].Id, 100m, 16m, null)], null))).EnsureOkAsync();
        var party = await ReadAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Customer", "Ports Company", "500600700", null, null, a["110302"].Id, 0, null, null)));
        var doc = await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/receivables"), new ApArDocumentRequest(party.Id, null, Today, null, null,
            [new ApArLineRequest("Crane rental", a["4102"].Id, 200m, 16m, null)], null)));
        await (await o.Client.PostAsJsonAsync(U(o, "/apar/payments"), new ApArPaymentRequest(doc.Document.Id, Today, 232m, "Cash", w.CashBox, null, null, 0, null))).EnsureOkAsync();
        return w;
    }

    [Fact]
    public async Task Account_statement_of_several_accounts_has_subtotals_and_a_grand_total()
    {
        // Revision 6 item 6: several accounts (chosen, code range, all), a subtotal per account, a grand total, exports; replaces the general ledger.
        var w = await LedgerAsync();
        var o = w.Owner;
        var a = w.Accounts;
        var q = $"from={D(Start)}&to={D(Today)}";

        // Two chosen accounts: one section each with its own opening, lines, subtotal and closing; the grand total adds them.
        var two = await ReadAsync<AccountStatementsReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/account-statements?accounts={a["110101"].Id},{a["5203"].Id}&{q}")));
        Assert.Equal(["110101", "5203"], two.Sections.Select(s => s.Account.Code));
        var cash = two.Sections[0];
        var single = await ReadAsync<LedgerReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/account-statement?account={a["110101"].Id}&{q}")));
        Assert.Equal((single.Opening, single.TotalDebit, single.TotalCredit, single.Closing), (cash.Opening, cash.TotalDebit, cash.TotalCredit, cash.Closing));
        Assert.Equal(single.Lines.TotalCount, cash.Lines.Count);
        Assert.Equal(cash.Closing, cash.Lines[^1].Balance);
        Assert.Equal(two.Sections.Sum(s => s.TotalDebit), two.TotalDebit);
        Assert.Equal(two.Sections.Sum(s => s.Closing), two.Closing);

        // A group and one of its own sub-accounts would count lines twice: refused.
        var overlap = await o.Client.GetAsync(U(o, $"/accounting/reports/account-statements?accounts={a["1101"].Id},{a["110101"].Id}&{q}"));
        Assert.Equal("statement.overlap", await overlap.ErrorCodeAsync());
        Assert.Equal("statement.noAccounts", await (await o.Client.GetAsync(U(o, $"/accounting/reports/account-statements?{q}"))).ErrorCodeAsync());

        // A code range and all accounts: accounts without sub-accounts with a balance or a movement; all accounts balance to zero.
        var range = await ReadAsync<AccountStatementsReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/account-statements?codeFrom=11&codeTo=1199&{q}")));
        Assert.All(range.Sections, s => Assert.StartsWith("11", s.Account.Code, StringComparison.Ordinal));
        Assert.Contains(range.Sections, s => s.Account.Code == "110101");
        var all = await ReadAsync<AccountStatementsReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/account-statements?allAccounts=true&{q}")));
        Assert.True(all.Sections.Count > 4);
        Assert.Equal(all.TotalDebit, all.TotalCredit);
        Assert.Equal(0m, all.Closing);
        Assert.All(all.Sections, s => Assert.True(s.Opening != 0 || s.Lines.Count > 0));

        // The export lists every section with its subtotal and the grand total.
        var csv = System.Text.Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o, $"/accounting/reports/account-statements/export?accounts={a["110101"].Id},{a["5203"].Id}&{q}&format=csv"))).Content.ReadAsByteArrayAsync());
        Assert.Contains("Subtotal 110101", csv, StringComparison.Ordinal);
        Assert.Contains("Subtotal 5203", csv, StringComparison.Ordinal);
        Assert.Contains("Grand total", csv, StringComparison.Ordinal);

        // Another company cannot read these accounts.
        var other = await f.SignUpOwnerAsync();
        Assert.False((await other.Client.GetAsync(U(other, $"/accounting/reports/account-statements?accounts={a["110101"].Id}&{q}"))).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Trial_balance_tree_sums_parents_from_children_and_keeps_the_totals()
    {
        // Revision 6 item 5: the trial balance as a tree — parents = sum of their children, links, chart order, level filter, same totals, export indentation.
        var w = await LedgerAsync();
        var o = w.Owner;
        var q = $"from={D(Start)}&to={D(Today)}";
        var flat = await ReadAsync<TrialBalanceReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance?{q}")));
        var tree = await ReadAsync<TrialBalanceReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance?{q}&tree=true")));

        // Same totals, still balancing; the level rows are exactly the flat rows.
        Assert.Equal(flat.Totals, tree.Totals);
        Assert.True(tree.Balanced);
        Assert.Equal(flat.Rows.Select(r => r.Account.Code).Order(StringComparer.Ordinal), tree.Rows.Where(r => !r.IsGroup).Select(r => r.Account.Code).Order(StringComparer.Ordinal));

        // Every parent row is the column-by-column sum of its children, and the roots add up to the totals.
        foreach (var parent in tree.Rows.Where(r => r.IsGroup))
        {
            var children = tree.Rows.Where(r => r.ParentId == parent.Account.Id).ToList();
            Assert.NotEmpty(children);
            Assert.Equal(children.Sum(c => c.MovementDebit), parent.MovementDebit);
            Assert.Equal(children.Sum(c => c.MovementCredit), parent.MovementCredit);
            Assert.Equal(children.Sum(c => c.ClosingDebit), parent.ClosingDebit);
            Assert.Equal(children.Sum(c => c.ClosingCredit), parent.ClosingCredit);
            Assert.All(children, c => Assert.Equal(parent.Level + 1, c.Level));
        }
        var roots = tree.Rows.Where(r => r.ParentId is null).ToList();
        Assert.Equal(tree.Totals.ClosingDebit, roots.Sum(r => r.ClosingDebit));
        Assert.Equal(tree.Totals.MovementCredit, roots.Sum(r => r.MovementCredit));

        // Depth-first chart order: each row comes after its parent.
        var seen = new HashSet<string>();
        foreach (var r in tree.Rows)
        {
            if (r.ParentId is not null) Assert.Contains(r.ParentId, seen);
            seen.Add(r.Account.Id);
        }

        // The level filter sets the depth of the tree; totals do not change.
        var level2 = await ReadAsync<TrialBalanceReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance?{q}&tree=true&level=2")));
        Assert.True(level2.Rows.Max(r => r.Level) <= 2);
        Assert.Equal(flat.Totals, level2.Totals);

        // The export keeps the indentation (two spaces per level).
        var csv = System.Text.Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance/export?{q}&tree=true&format=csv"))).Content.ReadAsByteArrayAsync());
        var deepest = tree.Rows.First(r => r.Level == 3);
        Assert.Contains("    " + deepest.Account.Code, csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task General_ledger_trial_balance_statements_and_reconciliation_FR116_FR121()
    {
        var w = await LedgerAsync();
        var o = w.Owner;
        var a = w.Accounts;

        // General ledger (FR-116): opening before the period, running balance across pages, closing.
        var gl = await ReadAsync<LedgerReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/general-ledger?account={a["110101"].Id}&from={D(Today)}&to={D(Today)}&pageSize=1&page=2")));
        Assert.Equal(1000m, gl.Opening);
        Assert.Equal(2, gl.Lines.TotalCount);
        Assert.Equal(232m, gl.TotalDebit);
        Assert.Equal(50m, gl.TotalCredit);
        Assert.Equal(1182m, gl.Closing);
        var second = Assert.Single(gl.Lines.Items);
        Assert.Equal(1182m, second.Balance);                  // 1,000 − 50 + 232
        Assert.Equal("ApArPayment", second.Source?.Type);     // drill-down to the source document (FR-121)
        Assert.NotNull(second.Source?.Id);
        // A parent account covers its descendants.
        var parent = await ReadAsync<LedgerReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/general-ledger?account={a["1101"].Id}&from={D(Start)}&to={D(Today)}")));
        Assert.Equal(1182m, parent.Closing);

        // Account statement by party (FR-120).
        var supplierParty = (await ReadAsync<PagedResult<ApArPartyResponse>>(await o.Client.GetAsync(U(o, "/apar/parties?role=supplier")))).Items.Single();
        var st = await ReadAsync<LedgerReport>(await o.Client.GetAsync(U(o,
            $"/accounting/reports/account-statement?account={a["2101"].Id}&partyType=ApArParty&party={supplierParty.Id}&from={D(Start)}&to={D(Today)}")));
        Assert.Equal(-116m, st.Closing);
        Assert.Equal("Al-Manaseer Fuel", st.Party?.Name);

        // Trial balance (FR-117) at level 1: balanced; opening-difference none.
        var tb = await ReadAsync<TrialBalanceReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance?from={D(Start)}&to={D(Today)}&level=1")));
        Assert.True(tb.Balanced);
        Assert.Equal(1648m, tb.Totals.ClosingDebit);
        Assert.Equal(1648m, tb.Totals.ClosingCredit);
        Assert.Equal(1498m, tb.Rows.Single(r => r.Account.Code == "1").ClosingDebit);
        Assert.Equal(148m, tb.Rows.Single(r => r.Account.Code == "2").ClosingCredit);
        Assert.All(tb.Rows, r => Assert.Equal(1, r.Level));
        Assert.Null(tb.OpeningDifference);
        var leaves = await ReadAsync<TrialBalanceReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance?from={D(Today)}&to={D(Today)}")));
        Assert.True(leaves.Balanced);
        Assert.Equal(1000m, leaves.Rows.Single(r => r.Account.Code == "110101").OpeningDebit);   // opening of the period
        Assert.Equal(232m, leaves.Rows.Single(r => r.Account.Code == "110101").MovementDebit);

        // Profit and loss (FR-118), with the cost-centre filter.
        var pl = await ReadAsync<ProfitLossReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/profit-loss?from={D(Start)}&to={D(Today)}")));
        Assert.Equal(200m, pl.TotalRevenue);
        Assert.Equal(150m, pl.TotalExpenses);
        Assert.Equal(50m, pl.NetProfit);
        Assert.Contains(pl.Expenses, r => r.Account.Code == "5" && r.Amount == 150m && !r.IsLeaf);
        var plCc = await ReadAsync<ProfitLossReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/profit-loss?from={D(Start)}&to={D(Today)}&costCentre={w.CostCentre}")));
        Assert.Equal(-50m, plCc.NetProfit);

        // Balance sheet (FR-119): current-year result within equity, balanced.
        var bs = await ReadAsync<BalanceSheetReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/balance-sheet?asOf={D(Today)}")));
        Assert.Equal(1498m, bs.TotalAssets);
        Assert.Equal(148m, bs.TotalLiabilities);
        Assert.Equal(50m, bs.CurrentYearResult);                 // every revenue and expense is dated today
        Assert.Equal(1350m, bs.TotalEquity);
        Assert.True(bs.Balanced);

        // Cost centres (FR-131) and the cash statement (FR-130).
        var cc = await ReadAsync<CostCentreReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/cost-centres?from={D(Start)}&to={D(Today)}")));
        Assert.Equal(50m, cc.Rows.Single(r => r.CostCentre != null).Expenses);
        Assert.Equal(200m, cc.Rows.Single(r => r.CostCentre == null).Revenue);
        var cash = await ReadAsync<CashBankStatement>(await o.Client.GetAsync(U(o, $"/accounting/reports/cash-bank-statement?cashBox={w.CashBox}&from={D(Start)}&to={D(Today)}")));
        Assert.Equal(0m, cash.Opening);
        Assert.Equal(1232m, cash.Receipts);
        Assert.Equal(50m, cash.Payments);
        Assert.Equal(1182m, cash.Closing);
        var balances = await ReadAsync<List<CashBankBalance>>(await o.Client.GetAsync(U(o, "/accounting/reports/cash-bank-balances")));
        Assert.Equal(1182m, balances.Single().Balance);

        // Reconciliation (FR-105): the opening on trade receivables has no customer (DT-24) → difference listed.
        var rec = await ReadAsync<ReconciliationReport>(await o.Client.GetAsync(U(o, "/accounting/reports/reconciliation")));
        Assert.False(rec.Reconciled);
        var receivables = rec.Rows.Single(r => r.Account.Code == "110301");
        Assert.Equal(300m, receivables.Difference);
        Assert.Equal("noParty", Assert.Single(receivables.Unmatched).Problem);
        Assert.Equal(0m, receivables.DocumentBalance);          // no transport invoice: the 300 opening is not explained by documents
        Assert.Equal(300m, receivables.DocumentDifference);
        var suppliers = rec.Rows.Single(r => r.Account.Code == "2101");
        Assert.Equal(-116m, suppliers.LedgerBalance);
        Assert.Equal(0m, suppliers.Difference);
        Assert.Equal(1, suppliers.Parties);
        Assert.Equal(0m, suppliers.DocumentDifference);          // the bill's remaining 116 matches the ledger

        // Exports (FR-121, FR-150): Excel and the printout with title, filters and totals.
        var xlsx = await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance/export?from={D(Start)}&to={D(Today)}&level=1"));
        await xlsx.EnsureOkAsync();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType?.MediaType);
        var html = await (await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance/export?from={D(Start)}&to={D(Today)}&level=1&format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("1,648.000", html, StringComparison.Ordinal);
        Assert.Contains(D(Start), html, StringComparison.Ordinal);
        foreach (var path in new[] { $"general-ledger/export?account={a["110101"].Id}&format=csv", "profit-loss/export?format=html", "balance-sheet/export?format=html",
                     "cost-centres/export", $"cash-bank-statement/export?cashBox={w.CashBox}&format=csv", "reconciliation/export" })
            await (await o.Client.GetAsync(U(o, "/accounting/reports/" + path))).EnsureOkAsync();
    }

    [Fact]
    public async Task Report_permissions()
    {
        var w = await ApArTests.WorldAsync(f);
        var o = w.Owner;
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.GetAsync(U(o, "/accounting/reports/trial-balance"))).StatusCode);
        await (await dispatcher.Client.GetAsync(U(o, "/accounting/reports/reconciliation"))).EnsureOkAsync();     // listed report (reports.view)
        await (await viewer.Client.GetAsync(U(o, "/accounting/reports/balance-sheet"))).EnsureOkAsync();          // acct.view
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync(U(o, "/accounting/maintenance/log"))).StatusCode);
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/accounting/reports/general-ledger?account={w.Accounts["110101"].Id}"))).StatusCode);
    }

    // ------------------------------------------------------------------ maintenance zone (FR-135, FR-136)

    [Fact]
    public async Task Maintenance_deletes_unlinked_entries_with_confirmation_and_logs_FR135_FR136()
    {
        var w = await LedgerAsync();
        var o = w.Owner;
        var a = w.Accounts;
        var journal = await ReadAsync<PagedResult<JournalEntrySummary>>(await o.Client.GetAsync(U(o, "/accounting/journal-entries?pageSize=50")));
        var manual = journal.Items.Single(e => e.SourceType == "Manual");
        var linked = journal.Items.First(e => e.SourceType == "SupplierBill");

        // Linked entries are refused; preview names the blocker and the word to type.
        var refused = await ReadAsync<MaintenancePreview>(await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/delete-entries/preview"),
            new MaintenanceRequest(null, null, [linked.Id], null)));
        Assert.NotEmpty(refused.Blockers);
        Assert.True(refused.ConfirmationWord is "حذف" or "DELETE");   // in the reader's language
        Assert.Equal("maintenance.refused", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/delete-entries"),
            new MaintenanceRequest("حذف", null, [linked.Id], null))).ProblemCodeAsync());

        // Delete-all keeps linked entries; the confirmation word must match.
        var preview = await ReadAsync<MaintenancePreview>(await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/delete-all-entries/preview"), new MaintenanceRequest(null, null, null, null)));
        Assert.Equal(1, preview.Counts["JournalEntries"]);            // the manual entry
        Assert.Equal(4, preview.LinkedEntriesKept);                   // bill, receivable, collection (documents) and the opening entry
        Assert.Empty(preview.Blockers);
        Assert.Equal("maintenance.confirmation", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/delete-all-entries"),
            new MaintenanceRequest("حذ", null, null, null))).ProblemCodeAsync());
        Assert.Equal("maintenance.refused", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/replace-chart"),
            new MaintenanceRequest("حذف", null, null, "template"))).ProblemCodeAsync());     // the chart is in use
        Assert.Equal("maintenance.refused", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/reset"),
            new MaintenanceRequest("حذف", null, null, null))).ProblemCodeAsync());           // entries linked to documents

        // The backup is offered first and logged.
        var backup = await o.Client.PostAsync(U(o, "/accounting/maintenance/backup"), null);
        await backup.EnsureOkAsync();
        Assert.Contains("\"accounts\"", await backup.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var done = await ReadAsync<MaintenanceResult>(await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/delete-all-entries"),
            new MaintenanceRequest("DELETE", "Re-import from zero", null, null)));
        Assert.Equal(1, done.Counts["JournalEntries"]);
        Assert.Equal(404, (int)(await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{manual.Id}"))).StatusCode);
        var tb = await ReadAsync<TrialBalanceReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/trial-balance?from={D(Start)}&to={D(Today)}")));
        Assert.True(tb.Balanced);
        Assert.Equal(1232m, tb.Rows.Single(r => r.Account.Code == "110101").ClosingDebit);  // opening 1,000 + collection 232; the manual −50 is gone
        Assert.DoesNotContain(tb.Rows, r => r.Account.Code == "5203");

        var log = await ReadAsync<List<MaintenanceLogEntry>>(await o.Client.GetAsync(U(o, "/accounting/maintenance/log")));
        Assert.Equal(["DeleteAllEntries", "Backup"], log.Select(l => l.Action).Take(2).ToArray());
        Assert.Equal(1, log[0].Counts["JournalEntries"]);

        var accountant = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Accountant);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/delete-all-entries/preview"),
            new MaintenanceRequest(null, null, null, null))).StatusCode);
    }

    [Fact]
    public async Task Maintenance_replace_chart_and_full_reset_respect_closed_periods_FR135()
    {
        // Replace the chart of a company that has posted nothing.
        var fresh = await ApArTests.WorldAsync(f);
        var replaced = await ReadAsync<MaintenanceResult>(await fresh.Owner.Client.PostAsJsonAsync(U(fresh.Owner, "/accounting/maintenance/replace-chart"),
            new MaintenanceRequest("حذف", null, null, "template")));
        Assert.True(replaced.Counts["TemplateAccounts"] > 0);
        var accounts = (await fresh.Owner.Client.GetFromJsonAsync<List<AccountResponse>>(U(fresh.Owner, "/accounting/accounts")))!;
        Assert.Equal(replaced.Counts["Accounts"], accounts.Count);
        Assert.DoesNotContain(accounts, x => x.Id == fresh.Accounts["110101"].Id);   // new accounts
        var keys = await ReadAsync<List<PostingKeyResponse>>(await fresh.Owner.Client.GetAsync(U(fresh.Owner, "/accounting/posting-keys")));
        Assert.All(keys, k => Assert.NotNull(k.Account));                            // mapping re-applied

        // Full reset: refused while a period with entries is closed; allowed after reopening.
        var w = await ApArTests.WorldAsync(f);
        var o = w.Owner;
        var a = w.Accounts;
        var lastMonth = Today.AddMonths(-1);
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            new ManualEntryRequest(lastMonth, "Bank charge", [Dr(a["5206"], 5m), Cr(a["110101"], 5m)], null))).EnsureOkAsync();
        var month = lastMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/periods/{month}/close"), new PeriodActionRequest("Month end", true))).EnsureOkAsync();
        var entries = await ReadAsync<PagedResult<JournalEntrySummary>>(await o.Client.GetAsync(U(o, "/accounting/journal-entries")));
        var preview = await ReadAsync<MaintenancePreview>(await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/delete-entries/preview"),
            new MaintenanceRequest(null, null, [entries.Items.Single().Id], null)));
        Assert.Contains(preview.Blockers, b => b.Contains(month, StringComparison.Ordinal));
        Assert.Equal("maintenance.refused", await (await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/reset"), new MaintenanceRequest("حذف", null, null, null))).ProblemCodeAsync());
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/periods/{month}/reopen"), new PeriodActionRequest("Correction", true))).EnsureOkAsync();
        var reset = await ReadAsync<MaintenanceResult>(await o.Client.PostAsJsonAsync(U(o, "/accounting/maintenance/reset"), new MaintenanceRequest("حذف", "Start again", null, null)));
        Assert.Equal(1, reset.Counts["JournalEntries"]);
        Assert.Equal(1, reset.Counts["Periods"]);
        Assert.Empty((await ReadAsync<PagedResult<JournalEntrySummary>>(await o.Client.GetAsync(U(o, "/accounting/journal-entries")))).Items);
        var periods = await ReadAsync<List<PeriodResponse>>(await o.Client.GetAsync(U(o, "/accounting/periods")));
        Assert.DoesNotContain(periods, p => p.Status == "Closed" || p.ClosedAt != null);   // period rows cleared: back to initial setup
    }
}
