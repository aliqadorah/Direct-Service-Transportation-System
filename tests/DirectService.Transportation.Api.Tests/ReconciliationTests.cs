using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Contracts.MasterData;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// RPT-23 sub-ledger reconciliation (owner testing 25 Oct 2026, DT-262): with manual entries on control accounts the ledger equals the party
/// balances (level "Documents", not "Differences"); every control account shows its documents figures the same way (110302 too, which documents
/// cannot rebuild); the documents difference is explained by the manual entries listed behind it; a real control-vs-parties difference (the
/// opening entry has no party) is level "Differences"; the export carries the same figures and entries.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReconciliationTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<T> OkAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Manual_entries_on_control_accounts_are_a_documents_difference_listed_behind_it_not_a_reconciliation_difference()
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var a = (await o.Client.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(x => x.Code);
        var customer = await OkAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"),
            new CustomerRequest("Reconciliation customer", null, null, "0781234567", null, null, null, null, 30, null, null, null, null, null)));
        var party = new PartyRequest("Customer", customer.Id);
        var onTrade = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), new ManualEntryRequest(Today, "Old balance",
            [new JournalLineRequest(a["110301"].Id, 200, 0, null, party, null), new JournalLineRequest(a["4101"].Id, 0, 200, null, null, null)], null)));
        var onOther = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), new ManualEntryRequest(Today, "Advance from customer",
            [new JournalLineRequest(a["110101"].Id, 200, 0, null, null, null), new JournalLineRequest(a["110302"].Id, 0, 200, null, party, null)], null)));
        // A voided manual entry and its reversal cancel out: neither is listed.
        var voided = await OkAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), new ManualEntryRequest(Today, "Typo",
            [new JournalLineRequest(a["110301"].Id, 75, 0, null, party, null), new JournalLineRequest(a["4101"].Id, 0, 75, null, null, null)], null)));
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-entries/{voided.Id}/void"), new VoidRequest("typo", voided.RowVersion))).EnsureOkAsync();

        var rec = await OkAsync<ReconciliationReport>(await o.Client.GetAsync(U(o, "/accounting/reports/reconciliation")));
        Assert.Equal((true, ReconciliationLevels.Documents), (rec.Reconciled, rec.Level));
        var trade = rec.Rows.Single(r => r.Account.Code == "110301");
        Assert.Equal((200m, 200m, 0m), (trade.LedgerBalance, trade.PartyBalances, trade.Difference));
        Assert.Equal((0m, 200m, 200m, 0m, 0m, true), (trade.DocumentBalance, trade.DocumentDifference, trade.ManualAmount, trade.OpeningAmount, trade.Unexplained, trade.DocumentsRebuilt));
        var listed = Assert.Single(trade.GapEntries!);
        Assert.Equal((onTrade.Number, "Manual", 200m), (listed.Entry.Number, listed.Kind, listed.Amount));

        // 110302 shows its documents figures like 110301 (the documents cannot rebuild it: what documents posted, i.e. 0).
        var other = rec.Rows.Single(r => r.Account.Code == "110302");
        Assert.Equal((-200m, -200m, 0m), (other.LedgerBalance, other.PartyBalances, other.Difference));
        Assert.Equal((0m, -200m, -200m, 0m, false), (other.DocumentBalance, other.DocumentDifference, other.ManualAmount, other.Unexplained, other.DocumentsRebuilt));
        Assert.Equal(onOther.Number, Assert.Single(other.GapEntries!).Entry.Number);
        Assert.All(rec.Rows, r => Assert.NotNull(r.DocumentBalance));
        Assert.All(rec.Rows, r => Assert.NotNull(r.DocumentDifference));

        // The export: same figures, the breakdown, the entries behind it and the verdict.
        var csv = Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o, "/accounting/reports/reconciliation/export?format=csv"))).Content.ReadAsByteArrayAsync());
        Assert.Contains("Of which manual entries", csv, StringComparison.Ordinal);
        Assert.Contains(onTrade.Number, csv, StringComparison.Ordinal);
        Assert.Contains(onOther.Number, csv, StringComparison.Ordinal);
        Assert.DoesNotContain(voided.Number, csv, StringComparison.Ordinal);
        Assert.Contains("Control accounts match the party balances", csv, StringComparison.Ordinal);

        // An opening on trade receivables has no customer (DT-24): a real difference, and the opening entry is listed as not a document.
        await (await o.Client.PutAsJsonAsync(U(o, "/accounting/opening-balances"), new OpeningBalancesRequest([new OpeningBalanceLine(a["110301"].Id, 50, 0), new OpeningBalanceLine(a["3101"].Id, 0, 50)])))
            .EnsureOkAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/opening-balances/post"), new PostOpeningRequest(Today))).EnsureOkAsync();
        rec = await OkAsync<ReconciliationReport>(await o.Client.GetAsync(U(o, "/accounting/reports/reconciliation")));
        Assert.Equal((false, ReconciliationLevels.Differences), (rec.Reconciled, rec.Level));
        trade = rec.Rows.Single(r => r.Account.Code == "110301");
        Assert.Equal((50m, 250m, 200m, 50m, 0m), (trade.Difference, trade.DocumentDifference, trade.ManualAmount, trade.OpeningAmount, trade.Unexplained));
        Assert.Contains(trade.GapEntries!, e => e.Kind == "Opening" && e.Amount == 50m);
        Assert.Equal(trade.DocumentDifference, trade.GapEntries!.Sum(e => e.Amount));
        Assert.Contains("differ from the party balances", Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o,
            "/accounting/reports/reconciliation/export?format=csv&asOf=" + Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))).Content.ReadAsByteArrayAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_manual_entries_the_report_is_ok()
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var rec = await OkAsync<ReconciliationReport>(await o.Client.GetAsync(U(o, "/accounting/reports/reconciliation")));
        Assert.Equal((true, ReconciliationLevels.Ok), (rec.Reconciled, rec.Level));
        Assert.All(rec.Rows, r => Assert.Equal((0m, 0m), (r.DocumentDifference, r.Unexplained)));
    }
}
