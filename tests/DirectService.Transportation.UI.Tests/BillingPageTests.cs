using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Web.Client.Pages.Billing;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Billing and cash screens (SCR-20 … SCR-28) against a fake API: what they request, what they show, which actions a role gets.</summary>
public sealed class BillingPageTests : PageTestBase
{
    private static readonly DateOnly Day = new(2026, 10, 8);

    internal static InvoiceResponse Invoice(string status = "Approved", string payment = "PartiallyPaid", string einvoice = "Submitted", string? number = "INV-02210") =>
        new("i1", number, InvoiceCodes.Trip, new Ref("cu1", "مناجم الفوسفات الأردنية"), Day, Day.AddDays(45), 3306.758m, 20m, 0, null, 3286.758m, 16m, 525.881m,
            3812.639m, 1500m, 0, 2312.639m, status, payment, false, InvoiceCodes.Claim, null, "سارة حداد", new DateTime(2026, 10, 8, 10, 2, 0, DateTimeKind.Utc),
            new DocRef("je1", "JV-00012"), false, null, einvoice, 18, DateTime.UtcNow, "AAAA");

    [Fact]
    public void Invoice_list_shows_rows_totals_and_state_tags()
    {
        Respond("invoices", new InvoicePage(new PagedResult<InvoiceResponse>([Invoice()], 1, 50, 1), new InvoiceListTotals(1, 3812.639m, 1500m, 0, 2312.639m)));
        var cut = Render<Invoices>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("INV-02210", cut.Markup));
        Assert.Contains("2,312.639", cut.Markup);
        Assert.Contains(L["st.partial"], cut.Markup);
        Assert.Contains(L["st.submitted"], cut.Markup);
        Assert.Contains(Requests, r => r.Path.StartsWith("invoices?", StringComparison.Ordinal) && r.Path.Contains("sort=-invoiceDate", StringComparison.Ordinal));
    }

    [Fact]
    public void Invoice_list_overdue_link_from_the_dashboard_filters_the_request()
    {
        Respond("invoices", new InvoicePage(new PagedResult<InvoiceResponse>([], 1, 50, 0), new InvoiceListTotals(0, 0, 0, 0, 0)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/invoices?overdue=true");
        var cut = Render<Invoices>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.Contains("overdue=true", StringComparison.Ordinal)));
        // UX-12: the drill-down key becomes the saved view in the address.
        var uri = Services.GetRequiredService<NavigationManager>().Uri;
        Assert.Contains("view=overdue", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("overdue=true", uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Invoice_list_restores_its_filters_from_the_address_UX12()
    {
        Respond("invoices", new InvoicePage(new PagedResult<InvoiceResponse>([Invoice()], 2, 50, 51), new InvoiceListTotals(51, 0, 0, 0, 0)));
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"/c/{Company}/invoices?view=draft&customer=cu1&q=INV&page=2&from=2026-10-01");
        var cut = Render<Invoices>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("invoices?", StringComparison.Ordinal)
            && r.Path.Contains("status=draft", StringComparison.Ordinal) && r.Path.Contains("customer=cu1", StringComparison.Ordinal)
            && r.Path.Contains("q=INV", StringComparison.Ordinal) && r.Path.Contains("page=2", StringComparison.Ordinal)
            && r.Path.Contains("from=2026-10-01", StringComparison.Ordinal)));
        Assert.Contains("view=draft", nav.Uri, StringComparison.Ordinal);
        Assert.Contains("customer=cu1", nav.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepted_invoice_cannot_be_voided_and_offers_receipt_and_credit_note()
    {
        Respond("invoices/i1/e-invoice", new EInvoiceSubmissionDetails(new EInvoiceSubmissionResponse("s1", EInvoiceCodes.Invoice, new DocRef("i1", "INV-02210"),
            new Ref("cu1", "x"), Day, 3812.639m, EInvoiceCodes.Submitted, 5512094, Guid.NewGuid(), "5512094", true, 1, null, [], false, DateTime.UtcNow, DateTime.UtcNow, null, "A"), null, null, null));
        Respond("invoices/i1", new InvoiceDetails(Invoice(), [], [], "ثلاثة آلاف", []));
        Respond("receipts", new ReceiptPage(new PagedResult<ReceiptResponse>([], 1, 200, 0), new CashListTotals(0, 0)));
        var cut = Render<InvoiceView>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "i1"));
        cut.WaitForAssertion(() => Assert.Contains(L["inv.acceptedBanner"], cut.Markup));
        var voidButton = cut.FindAll("button").Single(b => b.TextContent.Contains(L["inv.void"], StringComparison.Ordinal));
        Assert.True(voidButton.HasAttribute("disabled"));
        Assert.Contains(L["nav.newReceipt"], cut.Markup);
        Assert.Contains(L["cn.new"], cut.Markup);
        Assert.Contains(Requests, r => r.Path.Contains("receipts?invoice=i1", StringComparison.Ordinal));
    }

    /// <summary>Owner feedback 13 Oct 2026 (SCR-24): the invoice's paid and credited amounts are shown, and a returned trip is not offered again.</summary>
    [Fact]
    public void Credit_note_shows_paid_and_credited_amounts_and_leaves_out_returned_trips()
    {
        InvoiceLineResponse Line(string id, string receipt, decimal qty, decimal amount, decimal returned) =>
            new(id, 1, new Ref("t" + id, receipt), null, qty, 6m, amount, returned, Day, null, null, "سامي", "غور الصافي", null, null, null, null);
        var inv = Invoice() with { Total = 662.244m, PaidAmount = 331.122m, ReturnedAmount = 221.676m, RemainingAmount = 109.446m };
        Respond("invoices/i1", new InvoiceDetails(inv, [Line("l1", "DEMO-001", 31.85m, 191.1m, 31.85m), Line("l2", "DEMO-002", 31.4m, 188.4m, 0), Line("l3", "DEMO-003", 31.9m, 191.4m, 0)],
            [], "", []));
        Respond("invoices/calculate", new InvoiceCalculationResponse(0, 0, 0, 0, 16m, 0, 0, false, 3, Day, null, null, false));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/credit-notes/new?invoice=i1");
        var cut = Render<CreditNoteForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("DEMO-002", cut.Markup));
        Assert.DoesNotContain("DEMO-001", cut.Markup);
        Assert.Equal(2, cut.FindAll("tbody input[type=checkbox]").Count);
        var text = Text(cut);
        Assert.Contains("331.122", text);
        Assert.Contains("221.676", text);
        Assert.Contains("109.446", text);
        Assert.Contains(L.F("cn.returnedBefore", 1), text);
    }

    [Fact]
    public void Draft_invoice_offers_edit_delete_and_approve_but_a_viewer_gets_only_print()
    {
        Respond("invoices/i1", new InvoiceDetails(Invoice("Draft", "Draft", "NotRequired", null), [], [], "", []));
        Respond("receipts", new ReceiptPage(new PagedResult<ReceiptResponse>([], 1, 200, 0), new CashListTotals(0, 0)));
        var cut = Render<InvoiceView>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "i1"));
        cut.WaitForAssertion(() => Assert.Contains(L["inv.draftBanner"], cut.Markup));
        Assert.Contains(L["inv.approve"], cut.Markup);
        Assert.Contains(L["inv.deleteDraft"], cut.Markup);
    }

    [Fact]
    public void Manual_invoice_asks_the_server_for_totals_and_due_date()
    {
        Respond("invoices/calculate", new InvoiceCalculationResponse(870m, 0, 0, 870m, 16m, 139.2m, 1009.2m, false, 3, Day, Day.AddDays(45), 45, true));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/invoices/manual");
        var cut = Render<InvoiceForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("1,009.200", cut.Markup));
        Assert.Contains(L["inv.saveDraft"], cut.Markup);
        Assert.Contains(L["inv.saveApprove"], cut.Markup);
        Assert.Contains(L.F("inv.termsDays", 45), Text(cut));
        Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "invoices/calculate" && r.Body!.Contains("\"invoiceType\":\"Manual\"", StringComparison.Ordinal));
    }

    /// <summary>
    /// Owner testing 21 Oct 2026, item 1 (DT-228): a negative net offers "Repayment from the driver" — the whole shortfall by default, a part
    /// shows what is carried to the next settlement, more than the shortfall is refused on screen; method CliQ with its reference is sent.
    /// </summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Settlement_with_a_negative_net_offers_the_drivers_repayment(string language)
    {
        L.SetLanguage(language);
        Respond("settlements/preview", new SettlementPreview(new Ref("d1", "أحمد صالح"), "10-11111", Day,
            [new SettlementTripItem("t1", "TRP-1", Day, "غور الصافي ← العقبة", "بوتاس", 30m, 12.333m, 370m, false, null)],
            [new SettlementAdvanceItem("a1", "ADV-00411", Day, 400m)], [], 370m, 400m, -30m));
        Respond("settlements", new SettlementDetails(new SettlementResponse("s1", "STL-00001", new Ref("d1", "أحمد صالح"), Day, 370m, 400m, 0m, 0m, "CliQ",
            new MoneyAccountRef("BankAccount", "b1", "البنك العربي"), "CLIQ-55", "Active", null, false, null, DateTime.UtcNow, "RV", 10m, 20m, new DocRef("a2", "ADV-00412")),
            [], [], [], "words", []));
        var cut = Render<SettlementForm>(p => p.Add(x => x.Company, Company).Add(x => x.DriverId, "d1"));
        cut.WaitForAssertion(() => Assert.Contains(L.F("drv.netNegativeRepay", Fmt.Money(30m)), Text(cut)));
        Assert.Contains(L["drv.repayTitle"], Text(cut));
        AngleSharp.Dom.IElement Field(string label) => cut.Find("#" + cut.FindAll("label").First(l => l.TextContent.Contains(label, StringComparison.Ordinal)).GetAttribute("for"));
        AngleSharp.Dom.IElement Confirm() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["drv.confirmSettlement"], StringComparison.Ordinal));
        Assert.Equal("30.000", Field(L["drv.repayAmount"]).GetAttribute("value"));   // the whole shortfall by default
        Assert.False(Confirm().HasAttribute("disabled"));
        Assert.DoesNotContain(L["drv.repayCarried"], Text(cut));

        Act(() => Field(L["drv.repayAmount"]).Change("40"));                                        // above the shortfall
        cut.WaitForAssertion(() => Assert.Contains(L.F("drv.repayRange", Fmt.Money(30m)), Text(cut)));
        Assert.True(Confirm().HasAttribute("disabled"));

        Act(() => Field(L["drv.repayAmount"]).Change("10"));                                        // part: 20 carried
        cut.WaitForAssertion(() => Assert.Contains(L["drv.repayCarried"], Text(cut)));
        Assert.Contains(Fmt.Money(20m), Text(cut));
        AngleSharp.Dom.IElement method() => Field(L["cash.method"]);
        Act(() => method().Change(method().QuerySelectorAll("option").First(o => o.TextContent.Trim() == L["pay.CliQ"]).GetAttribute("value")));
        cut.WaitForAssertion(() => Assert.Contains(L["cash.bankAccount"], Text(cut)));
        Act(() => Field(L["cash.reference"]).Change("CLIQ-55"));
        Act(() => Confirm().Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "settlements"));
        var body = Requests.Last(r => r.Method == "POST" && r.Path == "settlements").Body!;
        Assert.Contains("\"repaidAmount\":10", body, StringComparison.Ordinal);
        Assert.Contains("\"paymentMethod\":\"CliQ\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reference\":\"CLIQ-55\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Settlement_with_a_positive_net_shows_no_repayment()
    {
        Respond("settlements/preview", new SettlementPreview(new Ref("d1", "أحمد صالح"), "10-11111", Day,
            [new SettlementTripItem("t1", "TRP-1", Day, "غور الصافي ← العقبة", "بوتاس", 30m, 12.333m, 370m, false, null)], [], [], 370m, 0m, 370m));
        var cut = Render<SettlementForm>(p => p.Add(x => x.Company, Company).Add(x => x.DriverId, "d1"));
        cut.WaitForAssertion(() => Assert.Contains(L["drv.netHelp"], Text(cut)));
        Assert.DoesNotContain(L["drv.repayTitle"], Text(cut));
    }

    [Fact]
    public void Receipt_form_lists_only_the_customers_open_invoices()
    {
        Respond("invoices", new InvoicePage(new PagedResult<InvoiceResponse>([Invoice(), Invoice("Closed", "Closed") with { Id = "i2", Number = "INV-02209", RemainingAmount = 0 }], 1, 200, 2),
            new InvoiceListTotals(2, 0, 0, 0, 0)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/receipts/new?customer=cu1&invoice=i1");
        var cut = Render<ReceiptForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("INV-02210", cut.Markup));
        Assert.DoesNotContain("INV-02209", cut.Markup);
        Assert.Contains(L["cash.cashBox"], cut.Markup);
    }

    /// <summary>
    /// Revision 6 item 4: ticking invoices fills the allocations oldest first up to the receipt amount; the user edits one; the summary shows
    /// selected, allocated and unallocated, and the request carries the allocations.
    /// </summary>
    [Fact]
    public void Receipt_form_allocates_ticked_invoices_oldest_first_and_posts_the_allocations()
    {
        var older = Invoice() with { Id = "i1", Number = "INV-1", InvoiceDate = Day.AddDays(-10), RemainingAmount = 100m };
        var newer = Invoice() with { Id = "i2", Number = "INV-2", InvoiceDate = Day, RemainingAmount = 300m };
        Respond("invoices", new InvoicePage(new PagedResult<InvoiceResponse>([newer, older], 1, 200, 2), new InvoiceListTotals(2, 0, 0, 0, 0)));
        Respond("receipts", new ReceiptDetails(new ReceiptResponse("r1", "RCV-00001", new Ref("cu1", "x"), null, Day, 250m, "Cash", new MoneyAccountRef("CashBox", "b1", "Box"),
            null, 0, null, "Active", null, false, null, DateTime.UtcNow, "RV"), "words", []));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/receipts/new?customer=cu1");
        var cut = Render<ReceiptForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("INV-2", cut.Markup));
        var rows = cut.FindAll("table.alloc tbody tr");
        Assert.Contains("INV-1", rows[0].TextContent, StringComparison.Ordinal);   // oldest first

        // Select all with an empty amount: the amount becomes the total and both invoices are covered.
        Act(() => cut.Find("table.alloc thead input[type=checkbox]").Change(true));
        Assert.Equal("400.000", ((AngleSharp.Html.Dom.IHtmlInputElement)AmountInput(cut)).Value.Replace(",", "", StringComparison.Ordinal));

        // A smaller amount fills the oldest first: 100 on INV-1, 150 on INV-2 (part payment).
        Act(() => AmountInput(cut).Change("250"));
        Assert.Equal("100.000", Allocated(cut, "INV-1"));
        Assert.Equal("150.000", Allocated(cut, "INV-2"));

        // The user changes the last allocation: 50 stays unallocated (on account).
        Act(() => cut.Find($"input[aria-label='{L.F("rcpt.allocatedTo", "INV-2")}']").Change("100"));
        Assert.Contains(L["rcpt.restOnAccount"], cut.Markup);

        // More than the invoice's balance: an error and no save.
        Act(() => cut.Find($"input[aria-label='{L.F("rcpt.allocatedTo", "INV-1")}']").Change("120"));
        Assert.Contains(L["rcpt.overRemaining"], cut.Markup);
        Assert.All(cut.FindAll("button").Where(b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal) || b.TextContent.Contains(L["cash.savePrint"], StringComparison.Ordinal)),
            b => Assert.True(b.HasAttribute("disabled")));

        // Back within the balance: saved with both allocations (100 + 100) and 50 left on account.
        Act(() => cut.Find($"input[aria-label='{L.F("rcpt.allocatedTo", "INV-1")}']").Change("100"));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal) && !b.TextContent.Contains(L["cash.savePrint"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "receipts"));
        var body = Requests.Single(r => r.Method == "POST" && r.Path == "receipts").Body!;
        Assert.Contains("\"allocations\":[{\"invoiceId\":\"i1\",\"amount\":100", body, StringComparison.Ordinal);
        Assert.Contains("{\"invoiceId\":\"i2\",\"amount\":100", body, StringComparison.Ordinal);
    }

    private AngleSharp.Dom.IElement AmountInput(IRenderedComponent<ReceiptForm> cut) =>
        cut.Find("#" + cut.FindAll("label").First(l => l.TextContent.Trim().StartsWith(L["inv.amount"], StringComparison.Ordinal)).GetAttribute("for"));

    private string Allocated(IRenderedComponent<ReceiptForm> cut, string number) =>
        ((AngleSharp.Html.Dom.IHtmlInputElement)cut.Find($"input[aria-label='{L.F("rcpt.allocatedTo", number)}']")).Value.Replace(",", "", StringComparison.Ordinal);
}
