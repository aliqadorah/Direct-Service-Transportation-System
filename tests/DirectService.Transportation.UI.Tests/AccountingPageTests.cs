using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Accounting screens (SCR-30 … SCR-45) against a fake API.</summary>
public sealed class AccountingPageTests : PageTestBase
{
    private static readonly AccountRef Cash = new("a2", "1101", "الصندوق الرئيسي", "Main cash box");
    private static readonly AccountRef Receivables = new("a3", "1110", "الذمم التجارية", "Trade receivables");

    public AccountingPageTests()
    {
        Respond("accounting/accounts/options", new[]
        {
            new AccountOption(Cash, "Asset", false, null),
            new AccountOption(Receivables, "Asset", true, "Customer"),
        });
        Respond("accounting/cost-centres/options", Array.Empty<object>());
        Respond("apar/parties/options", new[] { new ApArPartyOption("Customer", "cu1", "شركة البوتاس العربية", null, 30) });
    }

    [Fact]
    public void Chart_shows_the_tree_and_collapses_a_group()
    {
        Respond("accounting/accounts", new[]
        {
            new AccountResponse("a1", "11", "الأصول المتداولة", "Current assets", "Asset", null, null, 1, false, true, 0, 0, false, null, "Active", "A"),
            new AccountResponse("a2", "1101", "الصندوق الرئيسي", "Main cash box", "Asset", "a1", "11", 2, true, false, 500, 0, false, null, "Active", "A"),
        });
        var cut = Render<ChartOfAccounts>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("الصندوق الرئيسي", cut.Markup));
        Act(() => cut.Find($"button[aria-label='{L.F("acc.collapse", "11")}']").Click());
        Assert.DoesNotContain("الصندوق الرئيسي", cut.Markup);
    }

    [Fact]
    public void Empty_chart_offers_the_transport_template()
    {
        Respond("accounting/accounts", Array.Empty<object>());
        var cut = Render<ChartOfAccounts>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["acc.useTemplate"], cut.Markup));
    }

    [Fact]
    public void Journal_form_shows_the_difference_and_a_party_picker_on_control_accounts()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/journal/new");
        var cut = Render<JournalEntryForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["je.lines"], cut.Markup));
        Assert.Contains(L.F("je.difference", Fmt.Money(0)), Text(cut));
        Assert.DoesNotContain(L.F("je.lineParty", 1), cut.Markup);
    }

    /// <summary>Revision 6 item 3: Reference next to the description; "Save as draft" sends the entry to the drafts endpoint, not the journal.</summary>
    [Fact]
    public void Journal_form_saves_a_draft_with_the_reference()
    {
        var draft = new JournalDraftResponse("d1", new DateOnly(2026, 10, 17), "إيجار", "R-9", [], 0, 0, DateTime.UtcNow, "أنا", "RV");
        Respond("accounting/journal-drafts", draft);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/journal/new");
        var cut = Render<JournalEntryForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["je.reference"], cut.Markup));
        var reference = cut.FindAll("label").Single(l => l.TextContent.Trim() == L["je.reference"]).GetAttribute("for");
        Act(() => cut.Find("#" + reference).Change("R-9"));
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["je.saveDraft"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "accounting/journal-drafts"));
        Assert.Contains("\"reference\":\"R-9\"", Requests.Single(r => r.Method == "POST" && r.Path == "accounting/journal-drafts").Body!, StringComparison.Ordinal);
        Assert.DoesNotContain(Requests, r => r.Method == "POST" && r.Path == "accounting/journal-entries");
    }

    /// <summary>Revision 6 item 3: a draft row has no number, shows the Draft tag and the reference, and opens in the entry form.</summary>
    [Fact]
    public void Journal_list_shows_drafts_and_opens_them_in_the_form()
    {
        Respond("accounting/journal-entries", new PagedResult<JournalEntrySummary>(
        [
            new JournalEntrySummary("d1", "", new DateOnly(2026, 10, 17), "2026-10", "إيجار", "ACC", "Manual", true, "Draft", 250, 250, null, null, "R-9"),
            new JournalEntrySummary("e1", "JV-00001", new DateOnly(2026, 10, 16), "2026-10", "اتصالات", "ACC", "Manual", true, "Posted", 20, 20, null, null, null),
        ], 1, 50, 2));
        var cut = Render<JournalList>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("R-9", cut.Markup));
        Assert.Contains(L["st.draft"], cut.Markup);
        Assert.Contains(cut.FindAll("[role=tab]"), t => t.TextContent.Contains(L["je.draftsView"], StringComparison.Ordinal));
        Act(() => cut.FindAll("tbody tr")[0].Click());
        Assert.EndsWith($"/c/{Company}/accounting/journal/drafts/d1", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    /// <summary>Revision 6 item 5: the tree — a parent collapses its children, "collapse all" and "expand one level" work, the request asks for the tree.</summary>
    [Fact]
    public void Trial_balance_tree_expands_and_collapses()
    {
        var assets = new AccountRef("g1", "1", "الأصول", "Assets");
        var current = new AccountRef("g2", "11", "الأصول المتداولة", "Current assets");
        Respond("accounting/reports/trial-balance", new TrialBalanceReport(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 3, 3,
        [
            new TrialBalanceRow(assets, "Asset", 1, false, 0, 0, 100, 0, 100, 0, null, true),
            new TrialBalanceRow(current, "Asset", 2, false, 0, 0, 100, 0, 100, 0, "g1", true),
            new TrialBalanceRow(Cash, "Asset", 3, true, 0, 0, 100, 0, 100, 0, "g2"),
        ], new TrialBalanceTotals(0, 0, 100, 100, 100, 100), true, null));
        var cut = Render<TrialBalance>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("1101", cut.Markup));
        Assert.Contains(Requests, r => r.Path.StartsWith("accounting/reports/trial-balance?", StringComparison.Ordinal) && r.Path.Contains("tree=true", StringComparison.Ordinal));

        Act(() => cut.Find($"button[aria-label='{L.F("acc.collapse", "11")}']").Click());
        Assert.DoesNotContain("1101", cut.Markup);
        Assert.Contains("الأصول المتداولة", cut.Markup);

        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["rep.collapseAll"], StringComparison.Ordinal)).Click());
        Assert.DoesNotContain("الأصول المتداولة", cut.Markup);
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["rep.expandLevel"], StringComparison.Ordinal)).Click());
        Assert.Contains("الأصول المتداولة", cut.Markup);
        Assert.DoesNotContain("1101", cut.Markup);
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["rep.expandAll"], StringComparison.Ordinal)).Click());
        Assert.Contains("1101", cut.Markup);
    }

    /// <summary>Revision 6 item 6: the statement of several accounts — one section per account with its subtotal, the grand total, the request with all accounts.</summary>
    [Fact]
    public void Account_statement_shows_a_section_per_account_and_the_grand_total()
    {
        var day = new DateOnly(2026, 10, 10);
        LedgerLine Line(AccountRef a, decimal d, decimal c, decimal b) => new(day, new EntryLinkRef("e1", "JV-00001"), "حركة", null, a, null, null, null, d, c, b, "R-1");
        Respond("accounting/accounts", new[]
        {
            new AccountResponse("a2", "1101", "الصندوق الرئيسي", "Main cash box", "Asset", null, null, 1, true, false, 0, 0, false, null, "Active", "A"),
            new AccountResponse("a3", "1110", "الذمم التجارية", "Trade receivables", "Asset", null, null, 1, true, true, 0, 0, false, null, "Active", "A"),
        });
        Respond("accounting/reports/account-statements", new AccountStatementsReport(new DateOnly(2026, 10, 1), day, null, null,
        [
            new AccountStatementSection(Cash, "Asset", 100, 50, 0, 150, [Line(Cash, 50, 0, 150)]),
            new AccountStatementSection(Receivables, "Asset", 0, 0, 30, -30, [Line(Receivables, 0, 30, -30)]),
        ], 100, 50, 30, 120));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/statement?accounts=a2,a3");
        var cut = Render<AccountStatement>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("الذمم التجارية", cut.Markup));
        Assert.Contains(Requests, r => r.Path.StartsWith("accounting/reports/account-statements?", StringComparison.Ordinal) && r.Path.Contains("accounts=a2%2Ca3", StringComparison.Ordinal));
        Assert.Equal(2, cut.FindAll("table.grid").Count);
        Assert.Contains(L["stmt.subtotal"], cut.Markup);
        Assert.Contains(L["stmt.grandTotal"], cut.Markup);
        Assert.Contains("R-1", cut.Markup);
        Assert.Equal(3, cut.FindAll("[role=tab]").Count(t => t.TextContent.Contains(L["stmt.modeAccounts"], StringComparison.Ordinal)
            || t.TextContent.Contains(L["stmt.modeRange"], StringComparison.Ordinal) || t.TextContent.Contains(L["stmt.modeAll"], StringComparison.Ordinal)));
    }

    /// <summary>Revision 6 item 6: the old general-ledger address opens the account statement with the same query.</summary>
    [Fact]
    public void Old_general_ledger_address_redirects_to_the_statement()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/c/{Company}/accounting/ledger?account=a2&from=2026-10-01");
        Render<LedgerRedirect>(p => p.Add(x => x.Company, Company));
        Assert.EndsWith($"/c/{Company}/accounting/statement?account=a2&from=2026-10-01", nav.Uri, StringComparison.Ordinal);
    }

    /// <summary>Revision 6 item 7: supplier bills have a Deduction column (receivables do not); more than the line amount blocks saving.</summary>
    [Fact]
    public void Supplier_bill_form_has_a_deduction_per_line_that_cannot_exceed_the_amount()
    {
        Respond("apar/parties/options", new[] { new ApArPartyOption("Supplier", "s1", "المناصير", null, 30) });
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/c/{Company}/accounting/payables/new");
        var cut = Render<ApArDocumentForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["ap.deduction"], cut.Markup));
        Assert.Contains(L["ap.deductionHelp"], cut.Markup);
        Act(() => cut.Find($"input[aria-label='{L.F("ap.lineAmount", 1)}']").Change("100"));
        Act(() => cut.Find($"input[aria-label='{L.F("ap.lineDeduction", 1)}']").Change("150"));
        Assert.Contains(L["ap.deductionOver"], cut.Markup);
        Assert.True(cut.FindAll("button").Single(b => b.TextContent.Contains(L["ap.savePost"], StringComparison.Ordinal)).HasAttribute("disabled"));

        nav.NavigateTo($"/c/{Company}/accounting/receivables/new");
        var receivable = Render<ApArDocumentForm>(p => p.Add(x => x.Company, Company));
        receivable.WaitForAssertion(() => Assert.Contains(L["inv.items"], receivable.Markup));
        Assert.DoesNotContain(L["ap.lineDeduction"].Replace(" {0}", "", StringComparison.Ordinal), receivable.Markup);
    }

    /// <summary>Owner decision 17 Oct 2026: "Supplier deductions" not mapped → a clear message with a link to Account mapping, not a generic error.</summary>
    [Fact]
    public void Unmapped_supplier_deductions_key_shows_a_message_with_a_link_to_account_mapping()
    {
        Respond("apar/parties/options", new[] { new ApArPartyOption("Supplier", "s1", "المناصير", null, 30) });
        Respond("apar/bills", _ => new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)422)
        {
            Content = new System.Net.Http.StringContent("{\"status\":422,\"code\":\"apar.deductionKeyMissing\",\"detail\":\"x\"}", System.Text.Encoding.UTF8, "application/problem+json"),
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/payables/new");
        var cut = Render<ApArDocumentForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["ap.deduction"], cut.Markup));
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["ap.savePost"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["ap.deductionKeyMissing"], cut.Markup));
        Assert.NotNull(cut.Find($"a[href='/c/{Company}/settings/mapping']"));
    }

    [Fact]
    public void Trial_balance_highlights_a_mismatch()
    {
        var row = new TrialBalanceRow(Cash, "Asset", 2, true, 0, 0, 100, 0, 100, 0);
        Respond("accounting/reports/trial-balance", new TrialBalanceReport(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 2, 2, [row],
            new TrialBalanceTotals(0, 0, 100, 0, 100, 0), false, null));
        var cut = Render<TrialBalance>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["rep.tbMismatch"], cut.Markup));
        Assert.Contains("1101", cut.Markup);
    }

    [Fact]
    public void Receivables_list_requests_open_documents_of_the_receivable_kind()
    {
        Respond("apar/receivables", new ApArDocumentPage(new PagedResult<ApArDocumentResponse>([], 1, 50, 0), new ApArDocumentTotals(0, 0, 0)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/receivables");
        var cut = Render<ApAr>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["ap.noReceivables"], cut.Markup));
        Assert.Contains(Requests, r => r.Path.StartsWith("apar/receivables?", StringComparison.Ordinal) && r.Path.Contains("payment=open", StringComparison.Ordinal));
    }
}
