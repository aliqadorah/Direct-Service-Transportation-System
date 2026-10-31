using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Web.Client.Pages.Reports;
using DirectService.Shared.UI.Pages.Reports;
using DirectService.Transportation.Web.Client.Ui;
using DirectService.Shared.UI.Ui;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// RPT-23 (owner testing 25 Oct 2026, DT-262): red only for a control-vs-parties difference, amber when only documents differences exist,
/// every figure explained, the documents figures shown the same way for every control account, and the documents difference opening the
/// manual / opening entries behind it with links.
/// </summary>
public sealed class ReconciliationReportTests : PageTestBase
{
    private static readonly AccountRef Trade = new("a1", "110301", "ذمم عملاء النقل", "Trade receivables – transport customers");
    private static readonly AccountRef Other = new("a2", "110302", "ذمم مدينة أخرى", "Other receivables");

    private static ReconciliationRow Row(AccountRef account, decimal ledger, decimal parties, decimal documents, bool rebuilt, params DocumentGapEntry[] gap) =>
        new(account, "Customer", ledger, parties, ledger - parties, 1, [], documents, ledger - documents, rebuilt, gap.Where(g => g.Kind == "Manual").Sum(g => g.Amount),
            gap.Where(g => g.Kind == "Opening").Sum(g => g.Amount), ledger - documents - gap.Sum(g => g.Amount), gap);

    private static readonly DocumentGapEntry ManualOnTrade = new(new DateOnly(2026, 10, 25), new EntryLinkRef("e1", "JV-000007"), "Old balance", "Manual", 200);
    private static readonly DocumentGapEntry ManualOnOther = new(new DateOnly(2026, 10, 25), new EntryLinkRef("e2", "JV-000008"), "Advance from customer", "Manual", -200);

    private IRenderedComponent<Reconciliation> Show(string level, params ReconciliationRow[] rows) =>
        Render<Reconciliation>(p => p.Add(x => x.Report, new ReconciliationReport(new DateOnly(2026, 10, 25), rows, level != ReconciliationLevels.Differences, 0, level)));

    [Fact]
    public void Documents_differences_only_give_an_amber_note_not_the_red_banner()
    {
        var cut = Show(ReconciliationLevels.Documents, Row(Trade, 200, 200, 0, true, ManualOnTrade), Row(Other, -200, -200, 0, false, ManualOnOther));
        Assert.Empty(cut.FindAll(".banner.err"));
        Assert.Contains(L["rec.docsOnly"], cut.Find(".banner.warn").TextContent);
        Assert.DoesNotContain(L["rec.notOk"], cut.Markup);
    }

    [Fact]
    public void A_control_account_that_differs_from_its_parties_gives_the_red_banner_and_none_the_green_one()
    {
        var cut = Show(ReconciliationLevels.Differences, Row(Trade, 250, 200, 0, true, ManualOnTrade, new(new DateOnly(2026, 1, 1), new EntryLinkRef("e9", "JV-000001"), "Opening", "Opening", 50)));
        Assert.Contains(L["rec.notOk"], cut.Find(".banner.err").TextContent);
        Assert.Empty(cut.FindAll(".banner.ok"));
        Assert.Contains(L["rec.ok"], Show(ReconciliationLevels.Ok, Row(Trade, 0, 0, 0, true)).Find(".banner.ok").TextContent);
    }

    [Fact]
    public void Every_figure_is_explained()
    {
        var text = Text(Show(ReconciliationLevels.Ok, Row(Trade, 0, 0, 0, true)));
        foreach (var key in new[] { "rec.help.ledger", "rec.help.parties", "rec.help.difference", "rec.help.documents", "rec.help.documentDiff" })
            Assert.Contains(L[key], text);
    }

    [Fact]
    public void Every_control_account_shows_its_documents_figures_the_same_way()
    {
        var cut = Show(ReconciliationLevels.Documents, Row(Trade, 200, 200, 0, true, ManualOnTrade), Row(Other, -200, -200, 0, false, ManualOnOther));
        var boxes = cut.FindAll("section.box").Where(b => b.TextContent.Contains("1103", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, boxes.Count);
        Assert.Contains(Fmt.Money(200), boxes[0].TextContent);
        Assert.Contains(Fmt.Money(-200), boxes[1].TextContent);
        Assert.Single(boxes[1].TextContent.Split("—")[1..]);        // only the title separator, no "—" figure
        Assert.Contains(L["rec.posted"], boxes[1].TextContent);     // 110302: what documents posted, said so
        Assert.DoesNotContain(L["rec.posted"], boxes[0].TextContent);
    }

    [Fact]
    public void The_documents_difference_opens_the_entries_behind_it_with_links()
    {
        var cut = Show(ReconciliationLevels.Documents, Row(Trade, 200, 200, 0, true, ManualOnTrade), Row(Other, -200, -200, 0, false, ManualOnOther));
        Assert.DoesNotContain("JV-000007", cut.Markup);
        Act(() => cut.Find("button.linkbtn").Click());
        cut.WaitForAssertion(() => Assert.Contains("JV-000007", cut.Markup));
        var link = cut.FindAll("a").Single(a => a.TextContent.Contains("JV-000007", StringComparison.Ordinal));
        Assert.EndsWith("accounting/journal/e1", link.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Contains(L.F("rec.breakdown", Fmt.Money(200), Fmt.Money(0), Fmt.Money(0)), Text(cut));
        Assert.Equal("true", cut.Find("button.linkbtn").GetAttribute("aria-expanded"));
        Assert.DoesNotContain("JV-000008", cut.Markup);   // only the account opened

        Act(() => cut.Find("button.linkbtn").Click());
        cut.WaitForAssertion(() => Assert.DoesNotContain("JV-000007", cut.Markup));
    }
}
