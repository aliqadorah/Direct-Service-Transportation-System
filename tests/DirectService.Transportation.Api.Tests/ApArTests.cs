using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Acc;
using DirectService.Transportation.Web.Workers;
using DirectService.Transportation.Infrastructure.EInvoicing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B9 accounts payable and receivable through the API: parties (FR-122), supplier bills (FR-123) and receivable documents (FR-124) with
/// their entries, payments and collections (FR-125), returns with the mirror entry (FR-126), voids and printouts (FR-127), ageing
/// (FR-128), e-invoicing of receivables (D-15), permissions and isolation.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApArTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    internal sealed record World(SignedInUser Owner, Dictionary<string, AccountResponse> Accounts, string CashBox, string CostCentre);

    internal static async Task<World> WorldAsync(ApiFactory f)
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        await (await c.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var accounts = (await c.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(a => a.Code);
        var box = (await c.GetFromJsonAsync<List<CashBoxResponse>>(U(o, "/accounting/cash-boxes")))!.First();
        var cc = await ReadAsync<CostCentreResponse>(await c.PostAsJsonAsync(U(o, "/accounting/cost-centres"), new CostCentreRequest("CC-01", "Own fleet", null)));
        return new World(o, accounts, box.Id, cc.Id);
    }

    private static Task<JournalEntry> EntryAsync(ApiFactory f, string sourceType) =>
        f.DbAsync(db => db.Set<JournalEntry>().IgnoreQueryFilters().Include(e => e.Lines).Where(e => e.SourceType == sourceType).OrderByDescending(e => e.Id).FirstAsync());

    internal static async Task<ApArPartyResponse> SupplierAsync(World w, string name = "Al-Manaseer Fuel") =>
        await ReadAsync<ApArPartyResponse>(await w.Owner.Client.PostAsJsonAsync(U(w.Owner, "/apar/parties"),
            new ApArPartyRequest("Supplier", name, "100200300", "0791112223", "Amman", w.Accounts["2101"].Id, 30, null, null)));

    internal static async Task<ApArDocumentDetails> BillAsync(World w, ApArPartyResponse supplier) =>
        await ReadAsync<ApArDocumentDetails>(await w.Owner.Client.PostAsJsonAsync(U(w.Owner, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, "88412",
            [new ApArLineRequest("Diesel — October fleet", w.Accounts["5103"].Id, 3120m, 16m, w.CostCentre), new ApArLineRequest("Oil and filters", w.Accounts["5104"].Id, 410m, null, null)],
            null)));

    // ------------------------------------------------------------------ payables (FR-122, FR-123, FR-125 … FR-128)

    [Fact]
    public async Task Supplier_bill_posts_is_paid_returned_voided_and_aged_FR122_FR128()
    {
        var w = await WorldAsync(f);
        var o = w.Owner;
        // A supplier's control account must be a Supplier control account (FR-122).
        Assert.Equal("apar.controlAccount", await (await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Supplier", "Wrong", null, null, null, w.Accounts["110101"].Id, 0, null, null))).ProblemCodeAsync());
        var supplier = await SupplierAsync(w);
        Assert.Equal("Supplier", supplier.Role);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Supplier", "Al-Manaseer Fuel", null, null, null, w.Accounts["2101"].Id, 0, null, null))).StatusCode);   // name unique per role

        // Revenue accounts cannot be used on a bill.
        Assert.Equal("apar.lineAccount", await (await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("x", w.Accounts["4101"].Id, 10m, 16m, null)], null))).ProblemCodeAsync());

        var bill = await BillAsync(w, supplier);
        var d = bill.Document;
        Assert.Equal("BIL-00001", d.Number);
        Assert.Equal(3530.000m, d.Subtotal);
        Assert.Equal(564.800m, d.Tax);       // (3,120 + 410) × 16% (company rate when the line has none)
        Assert.Equal(4094.800m, d.Total);
        Assert.Equal(Today.AddDays(30), d.DueDate);   // payment terms
        Assert.Equal(ApArCodes.Open, d.PaymentStatus);
        var entry = await EntryAsync(f, "SupplierBill");
        Assert.Equal(4094.800m, entry.TotalDebit);
        Assert.Contains(entry.Lines, l => l.AccountId == entry.Lines.Single(x => x.Credit == 4094.800m).AccountId && l.PartyType == "ApArParty");
        Assert.Contains(entry.Lines, l => l.Debit == 564.800m);                                   // sales tax input
        Assert.Contains(entry.Lines, l => l.Debit == 3120m && l.CostCentreId != null);            // cost centre kept

        // Payment above the remaining balance refused; partial payment.
        Assert.Equal("apar.exceedsRemaining", await (await o.Client.PostAsJsonAsync(U(o, "/apar/payments"),
            new ApArPaymentRequest(d.Id, Today, 5000m, "Cash", w.CashBox, null, null, 0, null))).ProblemCodeAsync());
        var pay = await ReadAsync<ApArPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/payments"),
            new ApArPaymentRequest(d.Id, Today, 2000m, "Cash", w.CashBox, null, null, 2, null)));
        Assert.Equal("PAY-00001", pay.Payment.Number);
        Assert.False(string.IsNullOrWhiteSpace(pay.AmountInWords));
        var payEntry = await EntryAsync(f, "ApArPayment");
        Assert.Equal(2000m, payEntry.Lines.Single(l => l.PartyType == "ApArParty").Debit);
        var afterPay = await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/bills/{d.Id}")));
        Assert.Equal(2094.800m, afterPay.Document.RemainingAmount);
        Assert.Equal(ApArCodes.Partial, afterPay.Document.PaymentStatus);
        Assert.Single(afterPay.Payments);

        // Return of 100 before tax: tax share 16.000, mirror entry over the lines.
        var ret = await ReadAsync<ApArReturnDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/returns"), new ApArReturnRequest(d.Id, Today, 100m, "Wrong grade", null)));
        Assert.Equal("RTN-00001", ret.Return.Number);
        Assert.Equal(16.000m, ret.Return.Tax);
        Assert.Equal(116.000m, ret.Return.Total);
        var retEntry = await EntryAsync(f, "ApArReturn");
        Assert.Equal(116.000m, retEntry.Lines.Single(l => l.PartyType == "ApArParty").Debit);
        Assert.Equal(100m, retEntry.Lines.Where(l => l.PartyType is null && l.Credit > 0 && l.Credit != 16m).Sum(l => l.Credit));
        Assert.Equal(88.385m, retEntry.Lines.Single(l => l.CostCentreId != null).Credit);   // 100 × 3,120 / 3,530, largest remainder
        Assert.Equal(1978.800m, (await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/bills/{d.Id}")))).Document.RemainingAmount);

        // Void refused with active payments/returns (FR-127); void the payment restores the balance.
        Assert.Equal("apar.documentLinked", await (await o.Client.PostAsJsonAsync(U(o, $"/apar/bills/{d.Id}/void"), new CashVoidRequest("Duplicate", afterPay.Document.RowVersion)))
            .ProblemCodeAsync());
        var voided = await ReadAsync<ApArPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, $"/apar/payments/{pay.Payment.Id}/void"), new CashVoidRequest("Wrong box", pay.Payment.RowVersion)));
        Assert.Equal(CashCodes.Void, voided.Payment.Status);
        Assert.Equal(3978.800m, (await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/bills/{d.Id}")))).Document.RemainingAmount);

        // Ageing (FR-128): not yet due.
        var ageing = await ReadAsync<AgeingReport>(await o.Client.GetAsync(U(o, "/apar/ageing/payables")));
        Assert.Equal(3978.800m, ageing.Totals.NotDue);
        var late = await ReadAsync<AgeingReport>(await o.Client.GetAsync(U(o, $"/apar/ageing/payables?asOf={Today.AddDays(75):yyyy-MM-dd}")));
        Assert.Equal(3978.800m, late.Totals.Days31To60);
        Assert.Equal(supplier.Name, late.Rows.Single().Party.Name);
        await (await o.Client.GetAsync(U(o, "/apar/ageing/payables/export?format=csv"))).EnsureOkAsync();

        // List with totals and filters; print with amount in words, Copy on reprint.
        var list = await ReadAsync<ApArDocumentPage>(await o.Client.GetAsync(U(o, "/apar/bills?payment=open")));
        Assert.Equal(1, list.Totals.Count);
        Assert.Equal(3978.800m, list.Totals.Remaining);
        var html = await (await o.Client.GetAsync(U(o, $"/apar/bills/{d.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("BIL-00001", html, StringComparison.Ordinal);
        Assert.Contains("4,094.800", html, StringComparison.Ordinal);
        var again = await (await o.Client.GetAsync(U(o, $"/apar/bills/{d.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("class=\"copy\"", again, StringComparison.Ordinal);
        await (await o.Client.GetAsync(U(o, "/apar/bills/export"))).EnsureOkAsync();

        // Void the return, then the bill: reversal, number kept.
        var retNow = await ReadAsync<ApArReturnDetails>(await o.Client.GetAsync(U(o, $"/apar/returns/{ret.Return.Id}")));
        await (await o.Client.PostAsJsonAsync(U(o, $"/apar/returns/{ret.Return.Id}/void"), new CashVoidRequest("Entered twice", retNow.Return.RowVersion))).EnsureOkAsync();
        var current = await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/bills/{d.Id}")));
        var voidBill = await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, $"/apar/bills/{d.Id}/void"), new CashVoidRequest("Duplicate", current.Document.RowVersion)));
        Assert.Equal(ApArCodes.Void, voidBill.Document.Status);
        Assert.Equal("BIL-00001", voidBill.Document.Number);
        var reversed = await f.DbAsync(db => db.Set<JournalEntry>().IgnoreQueryFilters().CountAsync(e => e.SourceType == "SupplierBill" && e.ReversesEntryId != null));
        Assert.True(reversed >= 1);
        // A party with documents cannot be deleted.
        Assert.Equal("record.inUse", await (await o.Client.PostAsJsonAsync(U(o, $"/apar/parties/{supplier.Id}/delete"), new DeleteRequest("cleanup"))).ProblemCodeAsync());
        // Its control account is fixed once used (review M1).
        var otherControl = await ReadAsync<AccountResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts"),
            new AccountRequest("2106", "موردون آخرون", "Other suppliers", "Liability", w.Accounts["21"].Id, true, true, "Supplier", null)));
        Assert.Equal("record.inUse", await (await o.Client.PutAsJsonAsync(U(o, $"/apar/parties/{supplier.Id}"),
            new ApArPartyRequest("Supplier", supplier.Name, supplier.TaxNumber, supplier.Phone, supplier.Address, otherControl.Id, 30, null, supplier.RowVersion))).ProblemCodeAsync());
    }

    // ------------------------------------------------------------------ receivables and e-invoicing (FR-124, D-15)

    private Task<int> RunWorkerAsync() =>
        new EInvoiceWorker(f.Services.GetRequiredService<IServiceScopeFactory>(), f.Services.GetRequiredService<EInvoiceSignal>(), NullLogger<EInvoiceWorker>.Instance)
            .RunOnceAsync(CancellationToken.None);

    [Fact]
    public async Task Receivable_for_a_transport_customer_is_e_invoiced_and_returned_D15()
    {
        var w = await WorldAsync(f);
        var o = w.Owner;
        var settings = await ReadAsync<EInvoiceSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/e-invoicing")));
        await (await o.Client.PutAsJsonAsync(U(o, "/settings/e-invoicing"),
            new EInvoiceSettingsRequest(true, "Simulator", "12345678", "Sales", "4569874", "client-1", "secret-1", "Test", settings.RowVersion))).EnsureOkAsync();
        var customer = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"),
            new CustomerRequest("Aqaba Municipality", "300400500", null, "0781234567", null, null, "Aqaba", null, 15, null, null, null, null, null)));
        // Transport customers are offered as receivable parties (FR-122).
        var options = await ReadAsync<List<ApArPartyOption>>(await o.Client.GetAsync(U(o, "/apar/parties/options?role=customer")));
        Assert.Contains(options, x => x.Type == "Customer" && x.Name == "Aqaba Municipality");

        var doc = await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/receivables"), new ApArDocumentRequest(null, customer.Id, Today, null, "Contract 2026/14",
            [new ApArLineRequest("Truck rental — one week", w.Accounts["4102"].Id, 1400m, 16m, null)], null)));
        Assert.Equal("ARD-00001", doc.Document.Number);
        Assert.Equal(1624.000m, doc.Document.Total);
        Assert.Equal(Today.AddDays(15), doc.Document.DueDate);
        Assert.Equal(EInvoiceCodes.Queued, doc.Document.EInvoiceStatus);
        // The customer is now linked to a new accounting party (DT-101); the entry names that party on trade receivables.
        var linked = await ReadAsync<CustomerProfile>(await o.Client.GetAsync(U(o, $"/customers/{customer.Id}")));
        Assert.NotNull(linked.Customer.ApArParty);
        var entry = await EntryAsync(f, "Receivable");
        var debitAccount = entry.Lines.Single(l => l.Debit > 0).AccountId;
        Assert.Equal(w.Accounts["110301"].Code, (await f.DbAsync(db => db.Set<Account>().IgnoreQueryFilters().SingleAsync(a => a.Id == debitAccount))).Code);
        Assert.Equal(224.000m, entry.Lines.Single(l => l.Credit == 224.000m).Credit);   // sales tax output

        await RunWorkerAsync();
        var submission = await ReadAsync<EInvoiceSubmissionDetails>(await o.Client.GetAsync(U(o, $"/apar/receivables/{doc.Document.Id}/e-invoice")));
        Assert.Equal(EInvoiceCodes.Submitted, submission.Submission.Status);
        Assert.Contains("ARD-00001", submission.RequestPayload, StringComparison.Ordinal);
        var html = await (await o.Client.GetAsync(U(o, $"/apar/receivables/{doc.Document.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("<svg", html, StringComparison.Ordinal);   // QR printed once accepted

        // Accepted → void refused; a full return is the way out and is sent as a credit note referencing the document.
        var current = await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/receivables/{doc.Document.Id}")));
        Assert.Equal("apar.eInvoiced", await (await o.Client.PostAsJsonAsync(U(o, $"/apar/receivables/{doc.Document.Id}/void"),
            new CashVoidRequest("Cancelled", current.Document.RowVersion))).ProblemCodeAsync());
        var ret = await ReadAsync<ApArReturnDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/returns"), new ApArReturnRequest(doc.Document.Id, Today, 1400m, "Contract cancelled", null)));
        Assert.Equal(224.000m, ret.Return.Tax);
        Assert.Equal(1624.000m, ret.Return.Total);
        Assert.Equal(EInvoiceCodes.Queued, ret.Return.EInvoiceStatus);
        await RunWorkerAsync();
        var credit = await ReadAsync<EInvoiceSubmissionDetails>(await o.Client.GetAsync(U(o, $"/apar/returns/{ret.Return.Id}/e-invoice")));
        Assert.Equal(EInvoiceCodes.Submitted, credit.Submission.Status);
        Assert.Equal("CreditNote", credit.Submission.DocumentType);
        Assert.Contains("ARD-00001", credit.RequestPayload, StringComparison.Ordinal);
        Assert.Equal(0m, (await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/receivables/{doc.Document.Id}")))).Document.RemainingAmount);

        // A buyer without a tax number is accepted: the document is issued and sent without the TN identification (PRD 7.8.2, DT-102 as revised).
        var noTin = await ReadAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Customer", "Walk-in", null, null, null, w.Accounts["110302"].Id, 0, null, null)));
        var walkIn = await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/receivables"), new ApArDocumentRequest(noTin.Id, null, Today, null, null,
            [new ApArLineRequest("Scrap sale", w.Accounts["4102"].Id, 50m, 16m, null)], null)));
        Assert.Equal(8.000m, walkIn.Document.Tax);
        await RunWorkerAsync();
        var walkInSubmission = await ReadAsync<EInvoiceSubmissionDetails>(await o.Client.GetAsync(U(o, $"/apar/receivables/{walkIn.Document.Id}/e-invoice")));
        Assert.Equal(EInvoiceCodes.Submitted, walkInSubmission.Submission.Status);
        Assert.DoesNotContain("schemeID=\"TN\"", walkInSubmission.RequestPayload, StringComparison.Ordinal);
        // A supplier is not a receivable party.
        var supplier = await SupplierAsync(w);
        Assert.Equal("apar.partyRole", await (await o.Client.PostAsJsonAsync(U(o, "/apar/receivables"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("x", w.Accounts["4102"].Id, 50m, 0m, null)], null))).ProblemCodeAsync());
        var list = await ReadAsync<DirectService.Shared.Contracts.PagedResult<EInvoiceSubmissionResponse>>(await o.Client.GetAsync(U(o, "/apar/e-invoice-submissions?status=submitted")));
        Assert.Equal(3, list.TotalCount);
    }

    [Fact]
    public async Task Collections_and_receivables_ageing_include_transport_invoices_and_unapplied_receipts_FR128()
    {
        var w = await WorldAsync(f);
        var o = w.Owner;
        var party = await ReadAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Customer", "Ports Company", "500600700", null, null, w.Accounts["110302"].Id, 0, null, null)));
        var doc = await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/receivables"), new ApArDocumentRequest(party.Id, null, Today.AddDays(-40), Today.AddDays(-35),
            null, [new ApArLineRequest("Crane rental", w.Accounts["4102"].Id, 2000m, 16m, null)], null)));
        var col = await ReadAsync<ApArPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/payments"),
            new ApArPaymentRequest(doc.Document.Id, Today, 1320m, "Cash", w.CashBox, null, null, 0, null)));
        Assert.Equal(ApArCodes.Receivable, col.Payment.Kind);
        var colEntry = await EntryAsync(f, "ApArPayment");
        Assert.Equal(1320m, colEntry.Lines.Single(l => l.PartyType == "ApArParty").Credit);   // collection: Cr the customer's control account
        // Transport side: an on-account receipt is unapplied.
        var customer = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"),
            new CustomerRequest("Arab Potash", null, null, "0781234567", null, null, null, null, 30, null, null, null, null, null)));
        await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), new ReceiptRequest(customer.Id, Today, null, 75m, "Cash", w.CashBox, null, null, 0, null, null))).EnsureOkAsync();
        var ageing = await ReadAsync<AgeingReport>(await o.Client.GetAsync(U(o, "/apar/ageing/receivables")));
        var ports = ageing.Rows.Single(r => r.Party.Name == "Ports Company");
        Assert.Equal(1000.000m, ports.Amounts.Days31To60);   // 2,320 − 1,320, 36 days past due
        var potash = ageing.Rows.Single(r => r.Party.Name == "Arab Potash");
        Assert.Equal(75m, potash.Amounts.Unapplied);
        Assert.Equal(75m, ageing.Totals.Unapplied);
        var collections = await ReadAsync<ApArPaymentPage>(await o.Client.GetAsync(U(o, "/apar/payments?kind=receivables")));
        Assert.Equal(1320m, collections.Totals.Amount);
        Assert.Equal(0, (await ReadAsync<ApArPaymentPage>(await o.Client.GetAsync(U(o, "/apar/payments?kind=bills")))).Totals.Count);
    }

    // ------------------------------------------------------------------ permissions and isolation

    [Fact]
    public async Task Permissions_and_isolation_for_apar()
    {
        var w = await WorldAsync(f);
        var o = w.Owner;
        var supplier = await SupplierAsync(w);
        var bill = await BillAsync(w, supplier);
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        var accountant = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Accountant);
        await (await viewer.Client.GetAsync(U(o, $"/apar/bills/{bill.Document.Id}"))).EnsureOkAsync();     // acct.view reads
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(o, "/apar/payments"),
            new ApArPaymentRequest(bill.Document.Id, Today, 1m, "Cash", w.CashBox, null, null, 0, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.GetAsync(U(o, "/apar/bills"))).StatusCode);
        await (await dispatcher.Client.GetAsync(U(o, "/apar/ageing/payables"))).EnsureOkAsync();          // reports.view
        await (await accountant.Client.PostAsJsonAsync(U(o, "/apar/payments"), new ApArPaymentRequest(bill.Document.Id, Today, 1m, "Cash", w.CashBox, null, null, 0, null))).EnsureOkAsync();
        var clientId = Guid.NewGuid().ToString();
        var request = new ApArDocumentRequest(supplier.Id, null, Today, null, null, [new ApArLineRequest("Tyres", w.Accounts["5104"].Id, 10m, 0m, null)], clientId);
        Assert.Equal(HttpStatusCode.Created, (await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), request)).StatusCode);
        // A bill is not found as a receivable, and not in another tenant.
        Assert.Equal(HttpStatusCode.NotFound, (await o.Client.GetAsync(U(o, $"/apar/receivables/{bill.Document.Id}"))).StatusCode);
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/apar/bills/{bill.Document.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/apar/parties/{supplier.Id}"))).StatusCode);
    }

    // ------------------------------------------------------------------ supplier-bill line deductions (revision 6, item 7)

    [Fact]
    public async Task Supplier_bill_deductions_reduce_the_payable_keep_the_tax_and_post_to_supplier_deductions()
    {
        var w = await WorldAsync(f);
        var o = w.Owner;
        var supplier = await SupplierAsync(w);
        // 1000 at 16 % with a 100 deduction, 500 at 16 % with none: tax on the full amounts (240), payable 1000 + 500 + 240 − 100 = 1640.
        var bill = await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, "D-1",
            [new ApArLineRequest("Haulage", w.Accounts["5103"].Id, 1000m, 16m, null, 100m), new ApArLineRequest("Spare parts", w.Accounts["5104"].Id, 500m, 16m, null)], null)));
        var d = bill.Document;
        Assert.Equal((1500m, 240m, 100m, 1640m, 1640m), (d.Subtotal, d.Tax, d.Deduction, d.Total, d.RemainingAmount));
        Assert.Equal([100m, 0m], bill.Lines.Select(l => l.Deduction));

        // Entry: Dr lines 1500 + input tax 240 = Cr supplier 1640 + Cr supplier deductions (4105) 100.
        var entry = await EntryAsync(f, "SupplierBill");
        var ids = entry.Lines.Select(l => l.AccountId).ToList();
        var accounts = await f.DbAsync(db => db.Set<Account>().IgnoreQueryFilters().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code));
        Assert.Equal(1740m, entry.TotalDebit);
        Assert.Equal(100m, entry.Lines.Single(l => accounts[l.AccountId] == "4105").Credit);
        Assert.Equal(1640m, entry.Lines.Single(l => accounts[l.AccountId] == "2101").Credit);
        Assert.Contains("100.000", entry.Description, StringComparison.Ordinal);

        // List totals and export show the deductions.
        var list = await ReadAsync<ApArDocumentPage>(await o.Client.GetAsync(U(o, "/apar/bills")));
        Assert.Equal(100m, list.Totals.Deduction);
        var csv = System.Text.Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o, "/apar/bills/export?format=csv"))).Content.ReadAsByteArrayAsync());
        Assert.Contains("Deduction", csv, StringComparison.Ordinal);
        var html = await (await o.Client.GetAsync(U(o, $"/apar/bills/{d.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("Deduction", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deduction_cannot_exceed_the_line_and_only_supplier_bills_take_one()
    {
        var w = await WorldAsync(f);
        var o = w.Owner;
        var supplier = await SupplierAsync(w);
        var over = await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("Haulage", w.Accounts["5103"].Id, 100m, 16m, null, 100.001m)], null));
        Assert.Equal("validation.range", await over.ErrorCodeAsync());
        var negative = await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("Haulage", w.Accounts["5103"].Id, 100m, 16m, null, -1m)], null));
        Assert.Equal("validation.range", await negative.ErrorCodeAsync());

        var party = await ReadAsync<ApArPartyResponse>(await o.Client.PostAsJsonAsync(U(o, "/apar/parties"),
            new ApArPartyRequest("Customer", "Ports Company", "500600700", null, null, w.Accounts["110302"].Id, 0, null, null)));
        var receivable = await o.Client.PostAsJsonAsync(U(o, "/apar/receivables"), new ApArDocumentRequest(party.Id, null, Today, null, null,
            [new ApArLineRequest("Crane rental", w.Accounts["4102"].Id, 200m, 16m, null, 10m)], null));
        Assert.Equal("apar.deductionNotAllowed", await receivable.ErrorCodeAsync());

        // A company whose "Supplier deductions" key is not mapped (created before revision 6) cannot save a bill with a deduction, but still posts others.
        var email = o.Email;
        await f.DbAsync(async db =>
        {
            var companyId = await db.Set<DirectService.Platform.Domain.UserCompanyRole>().IgnoreQueryFilters().Where(r => r.User.Email == email).Select(r => r.CompanyId).FirstAsync();
            return await db.Set<PostingKeyMapping>().IgnoreQueryFilters().Where(m => m.PostingKey == "ACC.SupplierDeductions" && m.CompanyId == companyId).ExecuteDeleteAsync();
        });
        var missing = await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("Haulage", w.Accounts["5103"].Id, 100m, 16m, null, 5m)], null));
        Assert.Equal("apar.deductionKeyMissing", await missing.ErrorCodeAsync());
        await (await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("Haulage", w.Accounts["5103"].Id, 100m, 16m, null)], null))).EnsureOkAsync();
        var status = await ReadAsync<AccountingStatusResponse>(await o.Client.GetAsync(U(o, "/accounting/status")));
        Assert.True(status.PostingReady);   // the optional key never blocks posting
    }

    /// <summary>Owner decision 17 Oct 2026: a return gives back its share of the bill's deduction; a full return cancels the whole bill.</summary>
    [Fact]
    public async Task Returns_on_a_bill_with_deductions_give_back_the_deduction_in_proportion()
    {
        var w = await WorldAsync(f);
        var o = w.Owner;
        var supplier = await SupplierAsync(w);
        // 1000 (deduction 100) + 500 at 16 %: tax 240, payable 1640.
        var bill = (await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("Haulage", w.Accounts["5103"].Id, 1000m, 16m, null, 100m), new ApArLineRequest("Parts", w.Accounts["5104"].Id, 500m, 16m, null)], null)))).Document;

        // 600 of 1500 = 40 %: tax 96, deduction 40, the payable falls by 656.
        var first = (await ReadAsync<ApArReturnDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/returns"), new ApArReturnRequest(bill.Id, Today, 600m, "Short delivery", null)))).Return;
        Assert.Equal((600m, 96m, 40m, 656m), (first.Amount, first.Tax, first.Deduction, first.Total));
        var entry = await EntryAsync(f, "ApArReturn");
        var ids = entry.Lines.Select(l => l.AccountId).ToList();
        var codes = await f.DbAsync(db => db.Set<Account>().IgnoreQueryFilters().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code));
        Assert.Equal(40m, entry.Lines.Single(l => codes[l.AccountId] == "4105").Debit);
        Assert.Equal(656m, entry.Lines.Single(l => codes[l.AccountId] == "2101").Debit);
        Assert.Equal(entry.TotalDebit, entry.TotalCredit);
        Assert.Equal(984m, (await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/bills/{bill.Id}")))).Document.RemainingAmount);

        // The rest (900): the last return takes the remaining tax and deduction exactly; the bill is fully cancelled.
        var rest = (await ReadAsync<ApArReturnDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/returns"), new ApArReturnRequest(bill.Id, Today, 900m, "Rest", null)))).Return;
        Assert.Equal((144m, 60m, 984m), (rest.Tax, rest.Deduction, rest.Total));
        var after = (await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/bills/{bill.Id}")))).Document;
        Assert.Equal((0m, 1640m), (after.RemainingAmount, after.ReturnedAmount));

        // Voiding a return restores its net amount on the bill.
        var restNow = await ReadAsync<ApArReturnDetails>(await o.Client.GetAsync(U(o, $"/apar/returns/{rest.Id}")));
        await (await o.Client.PostAsJsonAsync(U(o, $"/apar/returns/{rest.Id}/void"), new CashVoidRequest("Entered by mistake", restNow.Return.RowVersion))).EnsureOkAsync();
        Assert.Equal(984m, (await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/bills/{bill.Id}")))).Document.RemainingAmount);

        // A full return in one go cancels the whole bill including its deduction.
        var bill2 = (await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, null,
            [new ApArLineRequest("Haulage", w.Accounts["5103"].Id, 100m, 0m, null, 30m)], null)))).Document;
        Assert.Equal(70m, bill2.Total);   // deduction larger than the tax: refused before this decision
        var full = (await ReadAsync<ApArReturnDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/returns"), new ApArReturnRequest(bill2.Id, Today, 100m, "Cancelled", null)))).Return;
        Assert.Equal((30m, 70m), (full.Deduction, full.Total));
    }

    /// <summary>Owner decision 17 Oct 2026: the supplier statement — amount, tax, deduction and net per document, running balance, totals, export.</summary>
    [Fact]
    public async Task Supplier_statement_shows_amount_tax_deduction_net_and_the_balance_owed()
    {
        var w = await WorldAsync(f);
        var o = w.Owner;
        var supplier = await SupplierAsync(w);
        var bill = (await ReadAsync<ApArDocumentDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/bills"), new ApArDocumentRequest(supplier.Id, null, Today, null, "S-1",
            [new ApArLineRequest("Haulage", w.Accounts["5103"].Id, 1000m, 16m, null, 100m)], null)))).Document;   // net 1060
        await ReadAsync<ApArReturnDetails>(await o.Client.PostAsJsonAsync(U(o, "/apar/returns"), new ApArReturnRequest(bill.Id, Today, 500m, "Half", null)));   // −530
        await (await o.Client.PostAsJsonAsync(U(o, "/apar/payments"), new ApArPaymentRequest(bill.Id, Today, 200m, "Cash", w.CashBox, null, null, 0, null))).EnsureOkAsync();

        var q = $"supplier={Uri.EscapeDataString(supplier.Id)}&from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}";
        var r = await ReadAsync<DirectService.Shared.Contracts.ReportResult>(await o.Client.GetAsync(U(o, $"/reports/standard/supplier-statement?{q}")));
        Assert.Equal(["date", "document", "description", "amount", "tax", "deduction", "net", "balance"], r.Columns.Select(c => c.Key));
        Assert.Equal(4, r.Rows.Count);   // opening, bill, return, payment
        decimal V(int row, int col) => ((System.Text.Json.JsonElement)r.Rows[row].Values[col]!).GetDecimal();
        Assert.Equal((1000m, 160m, 100m, 1060m, 1060m), (V(1, 3), V(1, 4), V(1, 5), V(1, 6), V(1, 7)));
        Assert.Equal((-500m, -80m, -50m, -530m, 530m), (V(2, 3), V(2, 4), V(2, 5), V(2, 6), V(2, 7)));
        Assert.Equal((-200m, 330m), (V(3, 6), V(3, 7)));
        decimal T(int col) => ((System.Text.Json.JsonElement)r.Totals![col]!).GetDecimal();
        Assert.Equal((500m, 80m, 50m, 330m, 330m), (T(3), T(4), T(5), T(6), T(7)));
        Assert.Equal(330m, (await ReadAsync<ApArDocumentDetails>(await o.Client.GetAsync(U(o, $"/apar/bills/{bill.Id}")))).Document.RemainingAmount);

        var csv = System.Text.Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o, $"/reports/standard/supplier-statement/export?{q}&format=csv"))).Content.ReadAsByteArrayAsync());
        Assert.Contains("Deduction", csv, StringComparison.Ordinal);
        Assert.Equal("validation.failed", await (await o.Client.GetAsync(U(o, "/reports/standard/supplier-statement"))).ProblemCodeAsync());
    }
}
