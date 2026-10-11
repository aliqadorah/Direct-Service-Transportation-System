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

    [Fact]
    public void Settlement_blocks_a_negative_net()
    {
        Respond("settlements/preview", new SettlementPreview(new Ref("d1", "أحمد صالح"), "10-11111", Day,
            [new SettlementTripItem("t1", "TRP-1", Day, "غور الصافي ← العقبة", "بوتاس", 30m, 12.333m, 370m, false, null)],
            [new SettlementAdvanceItem("a1", "ADV-00411", Day, 400m)], [], 370m, 400m, -30m));
        var cut = Render<SettlementForm>(p => p.Add(x => x.Company, Company).Add(x => x.DriverId, "d1"));
        cut.WaitForAssertion(() => Assert.Contains(L.F("drv.netNegative", Fmt.Money(-30m)), Text(cut)));
        var confirm = cut.FindAll("button").Single(b => b.TextContent.Contains(L["drv.confirmSettlement"], StringComparison.Ordinal));
        Assert.True(confirm.HasAttribute("disabled"));
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
}
