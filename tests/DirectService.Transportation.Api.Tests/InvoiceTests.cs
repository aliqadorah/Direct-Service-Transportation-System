using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B6 invoicing through the API: trip picker (FR-038), drafts reserving trips (FR-042), approval with number, entry and invoiced trips
/// (FR-043, FR-044), approval setting (FR-041), manual invoices (FR-039), credit notes with shares and rounding absorption (FR-049…052,
/// BR-025…027), void (FR-045), layouts and printing (FR-054…058), permissions. Uses the PRD examples (720 → 817.800; 180 → 204.450).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InvoiceTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    private sealed record World(SignedInUser Owner, CustomerResponse Customer, CustomerResponse Other, List<TripResponse> Trips);

    /// <summary>A company with the transport chart, a customer with four trips of 180.000 each (30 t × 6.000) and another customer.</summary>
    private async Task<World> WorldAsync(bool template = true)
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        if (template) await (await c.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        CustomerRequest Cust(string name) => new(name, "200014587", null, "0781234567", null, null, null, null, 30, null, null, null, null, null);
        var customer = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), Cust("Arab Potash")));
        var other = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), Cust("Jordan Phosphate")));
        var material = await ReadAsync<MaterialResponse>(await c.PostAsJsonAsync(U(o, "/materials"), new MaterialRequest("Potash", null, null, null)));
        var loading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/loading-sites"), new SiteRequest("Ghor Al-Safi", null, null, null)));
        var unloading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/unloading-sites"), new SiteRequest("Aqaba Port", null, null, null)));
        var types = (await c.GetFromJsonAsync<List<OwnershipTypeResponse>>(U(o, "/ownership-types")))!;
        var driver = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), new DriverRequest("Ahmad Saleh", "9871234560", "0791234567", null, null, "L-1", "C",
            Today.AddYears(1), "Cash", null, null, null, "10-11111", "Volvo", null, 32m, types.Single(t => t.IsBuiltIn).Id, null, Today.AddYears(1), Today.AddYears(1), Today.AddYears(1), null, null, null, null)));
        await (await c.PutAsJsonAsync(U(o, $"/monthly-rates/{Today.ToString("yyyy-MM", CultureInfo.InvariantCulture)}"),
            new MonthRatesRequest([new SiteRateRequest(loading.Id, 3.500m, 4.250m, null)]))).EnsureOkAsync();
        var trips = new List<TripResponse>();
        for (var i = 1; i <= 5; i++)
        {
            var cust = i == 5 ? other : customer;
            var r = new TripRequest($"T-{i}", Today, null, null, driver.Id, cust.Id, loading.Id, unloading.Id, material.Id, null, 30.000m, 30.000m, 6.000m, null, null, null, null, null, null);
            trips.Add((await ReadAsync<TripSaveResult>(await c.PostAsJsonAsync(U(o, "/trips"), r))).Trip);
        }
        return new World(o, customer, other, trips);
    }

    private static InvoiceRequest TripInvoice(World w, IEnumerable<TripResponse> trips, bool approve = false, decimal discount = 20m, decimal fees = 5m, string? rowVersion = null) =>
        new(w.Customer.Id, "Trip", Today, null, trips.Select(t => t.Id).ToList(), null, discount, fees, fees > 0 ? "Night loading" : null, null, null, approve, null, rowVersion);

    private Task<List<JournalEntry>> EntriesAsync(string sourceType) =>
        f.DbAsync(db => db.Set<JournalEntry>().IgnoreQueryFilters().Include(e => e.Lines).Where(e => e.SourceType == sourceType).OrderByDescending(e => e.Id).Take(4).ToListAsync());

    // ------------------------------------------------------------------ trip invoices (FR-038 … FR-044)

    [Fact]
    public async Task Draft_reserves_trips_and_approval_numbers_posts_and_invoices_PRD_example()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var picker = await ReadAsync<UnbilledTripsResponse>(await o.Client.GetAsync(U(o, $"/invoices/unbilled-trips?customer={w.Customer.Id}")));
        Assert.Equal(4, picker.Count);          // the other customer's trip is not listed (FR-038)
        Assert.Equal(720.000m, picker.Revenue);

        // Approval is on by default: Save creates a draft without a number (FR-041, FR-042).
        var draft = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(4))));
        Assert.Equal("Draft", draft.Invoice.Status);
        Assert.Null(draft.Invoice.Number);
        Assert.Equal(705.000m, draft.Invoice.BaseAmount);
        Assert.Equal(112.800m, draft.Invoice.Tax);
        Assert.Equal(817.800m, draft.Invoice.Total);
        Assert.Equal("Claim", draft.Invoice.LayoutType);   // rev. 5 default
        Assert.Equal(Today.AddDays(30), draft.Invoice.DueDate);   // payment terms 30 days
        Assert.Empty((await ReadAsync<UnbilledTripsResponse>(await o.Client.GetAsync(U(o, $"/invoices/unbilled-trips?customer={w.Customer.Id}")))).Trips);
        // A reserved trip cannot be edited (7.1.2).
        var trip = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{w.Trips[0].Id}")))).Trip;
        Assert.Equal("Invoiced", trip.Billing);
        Assert.Equal("trip.invoiced", await (await o.Client.PostAsJsonAsync(U(o, $"/trips/{trip.Id}/void"), new TripVoidRequest("x", trip.RowVersion))).ProblemCodeAsync());

        var approved = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, $"/invoices/{draft.Invoice.Id}/approve"), new StatusChangeRequest(draft.Invoice.RowVersion)));
        Assert.Equal("Approved", approved.Invoice.Status);
        Assert.Equal("INV-00001", approved.Invoice.Number);   // D-16 prefix
        Assert.Equal("Unpaid", approved.Invoice.PaymentState);
        Assert.NotNull(approved.Invoice.JournalEntry);
        Assert.Equal(4, approved.Lines.Count);
        Assert.Contains("dinar", approved.AmountInWords, StringComparison.OrdinalIgnoreCase);
        // Entry (8.6): Dr receivables 817.800 + discount 20.000 = Cr revenue 720.000 + other revenue 5.000 + tax 112.800.
        var e = (await EntriesAsync("Invoice"))[0];
        Assert.Equal(837.800m, e.TotalDebit);
        Assert.Contains(e.Lines, l => l.Debit == 817.800m && l.PartyType == "Customer");
        Assert.Contains(e.Lines, l => l.Credit == 112.800m);
        var history = await ReadAsync<List<HistoryEntry>>(await o.Client.GetAsync(U(o, $"/trips/{w.Trips[0].Id}/history")));
        // One readable line per event with the document as a link (DT-188); the invoice number is no longer a "reason".
        var invoiced = Assert.Single(history, h => h.Action == "Invoiced");
        Assert.Equal($"Test Owner invoiced the trip {w.Trips[0].ReceiptNo} on invoice INV-00001", invoiced.Text);
        Assert.Null(invoiced.Reason);
        Assert.Equal("Invoice", Assert.Single(invoiced.Links!).Kind);
        Assert.Equal("INV-00001", invoiced.Links![0].Text);
        var added = Assert.Single(history, h => h.Action == "AddedToInvoice");
        Assert.Equal($"Test Owner added the trip {w.Trips[0].ReceiptNo} to draft invoice INV-00001", added.Text);   // a draft then, numbered since
        Assert.Empty(added.Changes);   // the invoice is told by the sentence, not as "InvoiceId: — → 91"

        // A draft cannot be approved twice; an approved invoice is not edited.
        Assert.Equal("invoice.notDraft", await (await o.Client.PutAsJsonAsync(U(o, $"/invoices/{draft.Invoice.Id}"), TripInvoice(w, w.Trips.Take(4), rowVersion: approved.Invoice.RowVersion))).ProblemCodeAsync());

        // Lists: filters and totals (FR-047).
        var list = await ReadAsync<InvoicePage>(await o.Client.GetAsync(U(o, "/invoices?paid=unpaid")));
        Assert.Equal(817.800m, list.Totals.Remaining);
        Assert.Equal("INV-00001", (await ReadAsync<InvoicePage>(await o.Client.GetAsync(U(o, "/invoices?q=00001")))).Page.Items.Single().Number);
    }

    [Fact]
    public async Task Approval_setting_off_saves_approved_and_approval_is_blocked_without_mapping_or_in_closed_periods_FR041_FR044()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var ops = await ReadAsync<OperationSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/operations")));
        await (await o.Client.PutAsJsonAsync(U(o, "/settings/operations"), new OperationSettingsRequest(ops.BillOnUnloadedWeight, ops.PayDriverOnLoadedWeight, ops.ShortageTolerancePct,
            ops.LockTariffs, false, ops.AccrueDriverWage, true))).EnsureOkAsync();
        var direct = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(1), discount: 0, fees: 0)));
        Assert.Equal("Approved", direct.Invoice.Status);
        Assert.Equal(208.800m, direct.Invoice.Total);   // 180 + 16 %

        // Without the chart and mapping, approval is refused (FR-094) and the draft is kept.
        var bare = await WorldAsyncWithoutMapping();
        var draft = await ReadAsync<InvoiceDetails>(await bare.Owner.Client.PostAsJsonAsync(U(bare.Owner, "/invoices"),
            new InvoiceRequest(bare.Customer.Id, "Manual", Today, null, null, [new ManualLineRequest("Service", 1, 100m)], 0, 0, null, null, null, false, null, null)));
        var refused = await bare.Owner.Client.PostAsJsonAsync(U(bare.Owner, $"/invoices/{draft.Invoice.Id}/approve"), new StatusChangeRequest(draft.Invoice.RowVersion));
        Assert.Equal("posting.keysMissing", await refused.ProblemCodeAsync());
        var why = await refused.Content.ReadAsStringAsync();   // DT-224: the names of the Account mapping page, never the codes
        Assert.Contains("Transport revenue", why, StringComparison.Ordinal);
        Assert.DoesNotContain("TRN.", why, StringComparison.Ordinal);
        Assert.DoesNotContain("ACC.", why, StringComparison.Ordinal);

        // A future invoice date is refused; an extra fee needs a description.
        var bad = await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Skip(1).Take(1)) with { InvoiceDate = Today.AddDays(5), ExtraFeesDescription = null });
        var text = await bad.Content.ReadAsStringAsync();
        Assert.Contains("validation.futureDate", text, StringComparison.Ordinal);
        Assert.Contains("extraFeesDescription", text, StringComparison.Ordinal);
        // Another customer's trip cannot be invoiced.
        Assert.Equal("invoice.tripUnavailable", await (await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, [w.Trips[4]]))).ProblemCodeAsync());
    }

    private async Task<World> WorldAsyncWithoutMapping()
    {
        var o = await f.SignUpOwnerAsync();
        var customer = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"),
            new CustomerRequest("Cash Customer", null, null, null, null, null, null, null, 0, null, null, null, null, null)));
        return new World(o, customer, customer, []);
    }

    [Fact]
    public async Task Manual_invoice_and_draft_deletion_FR039_FR042()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var manual = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), new InvoiceRequest(w.Customer.Id, "Manual", Today, null, null,
            [new ManualLineRequest("Equipment move", 2, 350m), new ManualLineRequest("Night loading", 1, 170m)], 0, 0, null, "TaxInvoice", "PO-5531", true, null, null)));
        Assert.Equal(870.000m, manual.Invoice.Subtotal);
        Assert.Equal(139.200m, manual.Invoice.Tax);
        Assert.Equal(1009.200m, manual.Invoice.Total);
        Assert.Equal("Approved", manual.Invoice.Status);
        var bad = await o.Client.PostAsJsonAsync(U(o, "/invoices"), new InvoiceRequest(w.Customer.Id, "Manual", Today, null, null, [new ManualLineRequest("", 0, 10m)], 0, 0, null, null, null, false, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var draft = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(2))));
        await (await o.Client.PostAsJsonAsync(U(o, $"/invoices/{draft.Invoice.Id}/delete"), new InvoiceDeleteRequest("Wrong customer", draft.Invoice.RowVersion))).EnsureOkAsync();
        Assert.Equal(4, (await ReadAsync<UnbilledTripsResponse>(await o.Client.GetAsync(U(o, $"/invoices/unbilled-trips?customer={w.Customer.Id}")))).Count);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Client.GetAsync(U(o, $"/invoices/{draft.Invoice.Id}"))).StatusCode);
    }

    // ------------------------------------------------------------------ credit notes and void (FR-045 … FR-052)

    [Fact]
    public async Task Credit_notes_share_discount_fees_tax_absorb_rounding_and_void_restores_BR025_BR027()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var inv = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(4), approve: true)));
        var line = inv.Lines[0];
        var cn = await ReadAsync<CreditNoteDetails>(await o.Client.PostAsJsonAsync(U(o, "/credit-notes"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "Trip billed to the wrong customer", [new CreditLineRequest(line.Id, null)], null)));
        Assert.Equal("CRN-00001", cn.CreditNote.Number);
        Assert.Equal(5.000m, cn.CreditNote.DiscountShare);
        Assert.Equal(1.250m, cn.CreditNote.FeesShare);
        Assert.Equal(28.200m, cn.CreditNote.Tax);
        Assert.Equal(204.450m, cn.CreditNote.Total);
        var after = await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}")));
        Assert.Equal(613.350m, after.Invoice.RemainingAmount);
        Assert.Equal("PartiallyReturned", after.Invoice.PaymentState);
        // The returned trip is unbilled again (FR-051).
        Assert.Equal("Unbilled", (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{line.Trip!.Id}")))).Trip.Billing);
        // Mirror entry: Dr revenue 180, other revenue 1.250, tax 28.200; Cr discount 5.000, receivables 204.450.
        var e = (await EntriesAsync("CreditNote"))[0];
        Assert.Contains(e.Lines, l => l.Credit == 204.450m && l.PartyType == "Customer");
        // A returned line cannot be returned again.
        Assert.Equal("credit.lineReturned", await (await o.Client.PostAsJsonAsync(U(o, "/credit-notes"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "again", [new CreditLineRequest(line.Id, null)], null))).ProblemCodeAsync());

        // The invoice cannot be voided while a credit note is active (FR-045).
        Assert.Equal("invoice.hasPayments", await (await o.Client.PostAsJsonAsync(U(o, $"/invoices/{inv.Invoice.Id}/void"), new InvoiceVoidRequest("x", after.Invoice.RowVersion))).ProblemCodeAsync());

        // Returning everything still open leaves exactly 0.000 and closes the invoice (BR-027).
        var rest = await ReadAsync<CreditNoteDetails>(await o.Client.PostAsJsonAsync(U(o, "/credit-notes"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "Contract cancelled", inv.Lines.Skip(1).Select(l => new CreditLineRequest(l.Id, null)).ToList(), null)));
        Assert.Equal(613.350m, rest.CreditNote.Total);
        var closed = await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}")));
        Assert.Equal(0.000m, closed.Invoice.RemainingAmount);
        Assert.Equal("Closed", closed.Invoice.Status);

        // Void the second credit note: its trips are invoiced again and the balance restored (FR-052).
        var voided = await ReadAsync<CreditNoteDetails>(await o.Client.PostAsJsonAsync(U(o, $"/credit-notes/{rest.CreditNote.Id}/void"), new CreditNoteVoidRequest("Issued by mistake", rest.CreditNote.RowVersion)));
        Assert.Equal("Void", voided.CreditNote.Status);
        var reopened = await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}")));
        Assert.Equal(613.350m, reopened.Invoice.RemainingAmount);
        Assert.Equal("Approved", reopened.Invoice.Status);
        Assert.Equal("Invoiced", (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{inv.Lines[1].Trip!.Id}")))).Trip.Billing);

        // The first credit note cannot be voided once its trip is on another invoice.
        await (await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, [w.Trips[0]], discount: 0, fees: 0))).EnsureOkAsync();
        Assert.Equal("credit.tripReinvoiced", await (await o.Client.PostAsJsonAsync(U(o, $"/credit-notes/{cn.CreditNote.Id}/void"), new CreditNoteVoidRequest("x", cn.CreditNote.RowVersion))).ProblemCodeAsync());
    }

    /// <summary>
    /// Owner feedback 13 Oct 2026 (SCR-24, INV-00001): three trips 191.100 + 188.400 + 191.400 = 570.900, tax 16% → 662.244, half paid
    /// by a receipt of 331.122. Returning all three is refused because 331.122 is still unpaid, and the message says what was paid and
    /// credited; one trip (191.100 → 221.676) is accepted; a returned trip is no longer offered nor accepted again.
    /// </summary>
    [Fact]
    public async Task Credit_note_on_a_half_paid_invoice_is_capped_at_the_unpaid_amount_and_says_why_owner_feedback()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var c = o.Client;
        var trips = new List<TripResponse>();
        foreach (var (no, qty) in new[] { ("R-1", 31.850m), ("R-2", 31.400m), ("R-3", 31.900m) })
        {
            var full = w.Trips[0];
            var r = new TripRequest(no, Today, null, null, full.Driver.Id, w.Customer.Id, full.LoadingSite.Id, full.UnloadingSite.Id, full.Material.Id, null, qty, qty, 6.000m,
                null, null, null, null, null, null);
            trips.Add((await ReadAsync<TripSaveResult>(await c.PostAsJsonAsync(U(o, "/trips"), r))).Trip);
        }
        var inv = await ReadAsync<InvoiceDetails>(await c.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, trips, approve: true, discount: 0, fees: 0)));
        Assert.Equal((570.900m, 91.344m, 662.244m), (inv.Invoice.Subtotal, inv.Invoice.Tax, inv.Invoice.Total));
        var money = await ReadAsync<MoneyAccountOptions>(await c.GetAsync(U(o, "/accounting/money-accounts/options")));
        await (await c.PostAsJsonAsync(U(o, "/receipts"), new Contracts.Cash.ReceiptRequest(w.Customer.Id, Today, inv.Invoice.Id, 331.122m, "Cash", money.CashBoxes[0].Id,
            null, null, 0, null, null))).EnsureOkAsync();

        // All three: the proportional amount is 662.244 but only 331.122 is unpaid — refused, saying what was paid and credited.
        var all = inv.Lines.Select(l => new CreditLineRequest(l.Id, null)).ToList();
        using var req = new HttpRequestMessage(HttpMethod.Post, U(o, "/credit-notes/calculate")) { Content = JsonContent.Create(new CreditNoteRequest(inv.Invoice.Id, Today, "Returned", all, null)) };
        req.Headers.AcceptLanguage.ParseAdd("en");
        var refused = await c.SendAsync(req);
        Assert.Equal("credit.exceedsRemaining", await refused.ProblemCodeAsync());
        var detail = await refused.Content.ReadAsStringAsync();
        Assert.Contains("The credit note (662.244) is more than the unpaid remaining amount of the invoice (331.122)", detail, StringComparison.Ordinal);
        Assert.Contains("331.122 already paid by receipts and 0.000 already credited", detail, StringComparison.Ordinal);

        // One trip: its share of the invoice plus tax (191.100 × 1.16 = 221.676), within the unpaid amount.
        var preview = await ReadAsync<CreditNoteCalculation>(await c.PostAsJsonAsync(U(o, "/credit-notes/calculate"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "Returned", [all[0]], null)));
        Assert.Equal((191.100m, 30.576m, 221.676m, 331.122m), (preview.Subtotal, preview.Tax, preview.Total, preview.Remaining));
        await (await c.PostAsJsonAsync(U(o, "/credit-notes"), new CreditNoteRequest(inv.Invoice.Id, Today, "Returned", [all[0]], null))).EnsureOkAsync();
        var after = await ReadAsync<InvoiceDetails>(await c.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}")));
        Assert.Equal((331.122m, 221.676m, 109.446m), (after.Invoice.PaidAmount, after.Invoice.ReturnedAmount, after.Invoice.RemainingAmount));
        // The returned trip shows as fully returned (the screen leaves it out) and cannot be returned again.
        Assert.Equal(after.Lines[0].Quantity, after.Lines[0].ReturnedQuantity);
        Assert.Equal("credit.lineReturned", await (await c.PostAsJsonAsync(U(o, "/credit-notes/calculate"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "again", [all[0]], null))).ProblemCodeAsync());
    }

    [Fact]
    public async Task Void_reverses_releases_trips_and_keeps_the_number_FR045()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var inv = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(2), approve: true)));
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, $"/invoices/{inv.Invoice.Id}/void"), new InvoiceVoidRequest("", inv.Invoice.RowVersion))).StatusCode);
        var voided = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, $"/invoices/{inv.Invoice.Id}/void"), new InvoiceVoidRequest("Wrong prices", inv.Invoice.RowVersion)));
        Assert.Equal("Void", voided.Invoice.Status);
        Assert.Equal(inv.Invoice.Number, voided.Invoice.Number);
        Assert.Equal(4, (await ReadAsync<UnbilledTripsResponse>(await o.Client.GetAsync(U(o, $"/invoices/unbilled-trips?customer={w.Customer.Id}")))).Count);
        var entries = await EntriesAsync("Invoice");
        Assert.Equal("Reversed", entries[1].Status);
        Assert.Equal(entries[1].Id, entries[0].ReversesEntryId);
        // VOID stamp in the printout; the print is logged (FR-058).
        var html = await (await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("VOID", html, StringComparison.Ordinal);
        Assert.Contains(await ReadAsync<List<HistoryEntry>>(await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/history"))), h => h.Action == "Print");
    }

    [Fact]
    public async Task Issued_documents_keep_the_names_they_were_issued_with_FR163()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var inv = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(1), approve: true)));
        var customer = (await ReadAsync<CustomerProfile>(await o.Client.GetAsync(U(o, $"/customers/{w.Customer.Id}")))).Customer;
        await (await o.Client.PutAsJsonAsync(U(o, $"/customers/{w.Customer.Id}"), new CustomerRequest("APC Mining", customer.TaxNumber, null, customer.Phone, null, null, null,
            null, customer.PaymentTermsDays, null, null, null, null, customer.RowVersion))).EnsureOkAsync();

        // The approved invoice reprints with the name it was issued with; the customer list shows the new name (rename by id).
        var html = await (await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("Arab Potash", html, StringComparison.Ordinal);
        Assert.DoesNotContain("APC Mining", html, StringComparison.Ordinal);
        Assert.Equal("APC Mining", (await ReadAsync<CustomerProfile>(await o.Client.GetAsync(U(o, $"/customers/{w.Customer.Id}")))).Customer.Name);
        // A document issued after the rename carries the new name.
        var next = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Skip(1).Take(1), approve: true)));
        Assert.Contains("APC Mining", await (await o.Client.GetAsync(U(o, $"/invoices/{next.Invoice.Id}/print?format=html"))).Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ layouts and printing (FR-054 … FR-058)

    [Fact]
    public async Task Layouts_and_printing_claim_annex_tax_invoice_pdf_FR054_FR058()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var layout = await ReadAsync<InvoiceLayoutResponse>(await o.Client.GetAsync(U(o, "/invoice-layouts/claim")));
        Assert.Equal("Claim", layout.LayoutType);   // provisioned with the company
        Assert.Contains("amount", layout.AnnexColumns);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PutAsJsonAsync(U(o, "/invoice-layouts/claim"),
            new InvoiceLayoutRequest("Claim", null, "Route", ["receiptNo"], null, null, null, true, true, null))).StatusCode);   // amount column required
        var saved = await ReadAsync<InvoiceLayoutResponse>(await o.Client.PutAsJsonAsync(U(o, "/invoice-layouts/claim"),
            new InvoiceLayoutRequest("Claim statement — October", "Arab Transport Co.", "Route", ["receiptNo", "truckPlate", "billingQty", "amount"],
                new Dictionary<string, string> { ["truckPlate"] = "Truck" }, "Bank: Arab Bank", ["Accountant", "Customer"], false, true, layout.RowVersion)));
        Assert.Equal("Route", saved.Grouping);
        var preview = await o.Client.GetAsync(U(o, "/invoice-layouts/claim/preview"));
        Assert.Contains("Claim statement — October", await preview.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // DT-154: live preview of an unsaved layout (validated, nothing stored) and the built-in default with the stored row version.
        var draft = await o.Client.PostAsJsonAsync(U(o, "/invoice-layouts/claim/preview"),
            new InvoiceLayoutRequest("Draft title", null, "Route", ["receiptNo", "amount"], null, null, null, false, false, null));
        Assert.Contains("Draft title", await draft.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/invoice-layouts/claim/preview"),
            new InvoiceLayoutRequest("No amount", null, "Route", ["receiptNo"], null, null, null, false, false, null))).StatusCode);
        Assert.Equal("Route", (await ReadAsync<InvoiceLayoutResponse>(await o.Client.GetAsync(U(o, "/invoice-layouts/claim")))).Grouping);   // unchanged
        var builtIn = await ReadAsync<InvoiceLayoutResponse>(await o.Client.GetAsync(U(o, "/invoice-layouts/claim?builtIn=true")));
        Assert.Equal("LoadingSite", builtIn.Grouping);
        Assert.Equal(saved.RowVersion, builtIn.RowVersion);

        var inv = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(4), approve: true)));
        var html = await (await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("INV-00001", html, StringComparison.Ordinal);
        Assert.Contains("Truck", html, StringComparison.Ordinal);                         // custom label in the annex
        Assert.Contains("817.800", html, StringComparison.Ordinal);
        Assert.Contains("annex", html, StringComparison.Ordinal);
        Assert.Contains("200014587", html, StringComparison.Ordinal);                     // customer tax number
        var tax = await (await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=html&layout=TaxInvoice"))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("class=\"annex\"", tax, StringComparison.Ordinal);

        // PDF through headless Chromium: a real PDF (UX-23).
        var pdf = await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print"));
        await pdf.EnsureOkAsync();
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]), StringComparison.Ordinal);

        var cn = await ReadAsync<CreditNoteDetails>(await o.Client.PostAsJsonAsync(U(o, "/credit-notes"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "Returned", [new CreditLineRequest(inv.Lines[0].Id, null)], null)));
        var cnHtml = await (await o.Client.GetAsync(U(o, $"/credit-notes/{cn.CreditNote.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("INV-00001", cnHtml, StringComparison.Ordinal);   // original invoice number (FR-053)
        Assert.Contains("204.450", cnHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_fixes_trip_changed_after_credit_versions_layout_kept_and_html_encoded()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        // Text from users is encoded in printouts (no markup injection into the PDF engine).
        await (await o.Client.PutAsJsonAsync(U(o, $"/customers/{w.Customer.Id}"), new CustomerRequest("<img src=x onerror=alert(1)>", "200014587", null, "0781234567",
            null, null, null, null, 30, null, null, null, null, w.Customer.RowVersion))).EnsureOkAsync();
        var draft = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(2)) with { LayoutType = "TaxInvoice" }));
        var html = await (await o.Client.GetAsync(U(o, $"/invoices/{draft.Invoice.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x", html, StringComparison.Ordinal);

        // An edit without a layout keeps the stored one (L1); deleting a draft needs its version (L7).
        var edited = await ReadAsync<InvoiceDetails>(await o.Client.PutAsJsonAsync(U(o, $"/invoices/{draft.Invoice.Id}"), TripInvoice(w, w.Trips.Take(2), rowVersion: draft.Invoice.RowVersion)));
        Assert.Equal("TaxInvoice", edited.Invoice.LayoutType);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, $"/invoices/{draft.Invoice.Id}/delete"), new InvoiceDeleteRequest("x", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.Client.PostAsJsonAsync(U(o, $"/invoices/{draft.Invoice.Id}/delete"), new InvoiceDeleteRequest("x", draft.Invoice.RowVersion))).StatusCode);

        // Layouts: an unknown type is a 400 (L3); saving over a stored layout needs its version (L8).
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.GetAsync(U(o, "/invoice-layouts/receipt"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PutAsJsonAsync(U(o, "/invoice-layouts/claim"),
            new InvoiceLayoutRequest("Claim", null, null, ["amount"], null, null, null, true, true, null))).StatusCode);

        // A credit note cannot be voided once a returned trip was edited (M5).
        var inv = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, $"/invoices/{draft.Invoice.Id}/approve"), new StatusChangeRequest(edited.Invoice.RowVersion)));
        var cn = await ReadAsync<CreditNoteDetails>(await o.Client.PostAsJsonAsync(U(o, "/credit-notes"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "Wrong price", [new CreditLineRequest(inv.Lines[0].Id, null)], null)));
        var t = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{inv.Lines[0].Trip!.Id}")))).Trip;
        await (await o.Client.PutAsJsonAsync(U(o, $"/trips/{t.Id}"), new TripRequest(t.ReceiptNo, t.TripDate, null, null, t.Driver.Id, t.Customer.Id, t.LoadingSite.Id,
            t.UnloadingSite.Id, t.Material.Id, null, t.LoadedWeight, t.UnloadedWeight, 6.500m, null, null, null, null, null, t.RowVersion))).EnsureOkAsync();
        Assert.Equal("credit.tripChanged", await (await o.Client.PostAsJsonAsync(U(o, $"/credit-notes/{cn.CreditNote.Id}/void"),
            new CreditNoteVoidRequest("x", cn.CreditNote.RowVersion))).ProblemCodeAsync());
    }

    [Fact]
    public async Task Permissions_and_isolation_for_invoicing()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var inv = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(1), approve: true, discount: 0, fees: 0)));
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        var accountant = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Accountant);
        await (await viewer.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}"))).EnsureOkAsync();
        await (await viewer.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=html"))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Skip(1).Take(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.GetAsync(U(o, "/invoices"))).StatusCode);
        await (await accountant.Client.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Skip(1).Take(1), approve: true))).EnsureOkAsync();
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/invoices/{inv.Invoice.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/invoices/{inv.Invoice.Id}/print?format=html"))).StatusCode);
        // Trips of another tenant cannot be put on an invoice (tokens resolve through the company's data only).
        var otherCustomer = await ReadAsync<CustomerResponse>(await other.Client.PostAsJsonAsync(U(other, "/customers"),
            new CustomerRequest("X", null, null, null, null, null, null, null, 0, null, null, null, null, null)));
        var steal = await other.Client.PostAsJsonAsync(U(other, "/invoices"), new InvoiceRequest(otherCustomer.Id, "Trip", Today, null, [w.Trips[2].Id], null, 0, 0, null, null, null, false, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, steal.StatusCode);
    }

    // ------------------------------------------------------------------ screen support (step 8, DT-146 … DT-149)

    [Fact]
    public async Task Screens_get_live_totals_list_exports_receipts_of_an_invoice_money_accounts_and_an_unlogged_preview()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var c = o.Client;
        // Live totals with the PRD example: 720 − 20 + 5 = 705, tax 112.800, total 817.800; due date from the customer's 30-day terms (DT-148).
        var calc = await ReadAsync<InvoiceCalculationResponse>(await c.PostAsJsonAsync(U(o, "/invoices/calculate"),
            new InvoiceCalculationRequest("Trip", w.Customer.Id, Today, w.Trips.Take(4).Select(t => t.Id).ToList(), null, 20m, 5m)));
        Assert.Equal((720.000m, 705.000m, 112.800m, 817.800m), (calc.Subtotal, calc.BaseAmount, calc.Tax, calc.Total));
        Assert.Equal(Today.AddDays(30), calc.DueDate);
        Assert.True(calc.ApprovalRequired);
        var manual = await ReadAsync<InvoiceCalculationResponse>(await c.PostAsJsonAsync(U(o, "/invoices/calculate"),
            new InvoiceCalculationRequest("Manual", null, Today, null, [new ManualLineRequest("Transport", 2m, 350m), new ManualLineRequest("Night loading", 1m, 170m)], null, null)));
        Assert.Equal(1009.200m, manual.Total);

        var inv = await ReadAsync<InvoiceDetails>(await c.PostAsJsonAsync(U(o, "/invoices"), TripInvoice(w, w.Trips.Take(4), approve: true)));
        // Credit-note preview with the PRD example (one trip of 180.000 → 204.450); nothing is saved.
        var preview = await ReadAsync<CreditNoteCalculation>(await c.PostAsJsonAsync(U(o, "/credit-notes/calculate"),
            new CreditNoteRequest(inv.Invoice.Id, Today, "Returned", [new CreditLineRequest(inv.Lines[0].Id, null)], null)));
        Assert.Equal((176.250m, 28.200m, 204.450m), (preview.BaseAmount, preview.Tax, preview.Total));
        Assert.Empty((await c.GetFromJsonAsync<Contracts.Common.PagedResult<CreditNoteResponse>>(U(o, "/credit-notes")))!.Items);

        // List exports (FR-047, FR-165).
        var csv = await c.GetAsync(U(o, "/invoices/export?format=csv"));
        await csv.EnsureOkAsync();
        Assert.Contains("INV-00001", await csv.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await (await c.GetAsync(U(o, "/credit-notes/export"))).EnsureOkAsync();

        // Cash boxes and banks for cash documents (DT-147), and the receipts of one invoice (FR-048).
        var money = await ReadAsync<MoneyAccountOptions>(await c.GetAsync(U(o, "/accounting/money-accounts/options")));
        Assert.NotEmpty(money.CashBoxes);
        Assert.Equal(Today.AddDays(1), money.Today);
        // Postable accounts with their control-party type for the accounting forms (DT-147).
        var accounts = await ReadAsync<List<AccountOption>>(await c.GetAsync(U(o, "/accounting/accounts/options")));
        Assert.Contains(accounts, a => a.IsControlAccount && a.ControlPartyType == "Customer");
        await (await c.GetAsync(U(o, "/accounting/cost-centres/options"))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U(o, "/receipts"), new Contracts.Cash.ReceiptRequest(w.Customer.Id, Today, inv.Invoice.Id, 100m, "Cash", money.CashBoxes[0].Id, null, null, 0, null, null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U(o, "/receipts"), new Contracts.Cash.ReceiptRequest(w.Customer.Id, Today, null, 50m, "Cash", money.CashBoxes[0].Id, null, null, 0, null, null))).EnsureOkAsync();
        var onInvoice = await ReadAsync<Contracts.Cash.ReceiptPage>(await c.GetAsync(U(o, $"/receipts?invoice={inv.Invoice.Id}")));
        Assert.Equal(100m, Assert.Single(onInvoice.Page.Items).Amount);

        // The on-screen preview is not a print (FR-058); the printout is, and a voucher's second print is a Copy (FR-090).
        async Task<int> Prints() => (await ReadAsync<List<HistoryEntry>>(await c.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/history")))).Count(h => h.Action == "Print");
        var before = await Prints();
        await (await c.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=html&preview=true"))).EnsureOkAsync();
        Assert.Equal(before, await Prints());
        await (await c.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=html"))).EnsureOkAsync();
        Assert.Equal(before + 1, await Prints());
        // A PDF is always a logged print, even when asked as a preview (review H1).
        await (await c.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}/print?format=pdf&preview=true"))).EnsureOkAsync();
        Assert.Equal(before + 2, await Prints());
        var receipt = onInvoice.Page.Items[0].Id;
        var first = await (await c.GetAsync(U(o, $"/receipts/{receipt}/print?format=html&preview=true"))).Content.ReadAsStringAsync();
        var printed = await (await c.GetAsync(U(o, $"/receipts/{receipt}/print?format=html"))).Content.ReadAsStringAsync();
        var again = await (await c.GetAsync(U(o, $"/receipts/{receipt}/print?format=html&preview=true"))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("class=\"copy\"", first, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"copy\"", printed, StringComparison.Ordinal);
        Assert.Contains("class=\"copy\"", again, StringComparison.Ordinal);   // a reprint would be a copy, and the preview shows it
    }
}
