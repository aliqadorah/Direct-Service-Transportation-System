using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B8 cash, drivers and partners through the API: receipts against invoices and on account (FR-067 … FR-071), advances with edit and
/// void (FR-072 … FR-075), settlements per 8.5 with deductions, accrual-aware entry, locks and void (FR-076 … FR-082, BR-031/032),
/// statements (FR-083, FR-088), partner payments capped at the balance due (FR-084 … FR-087), vouchers with "Copy" (FR-090), permissions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CashTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    internal sealed record World(SignedInUser Owner, CustomerResponse Customer, PartnerResponse Partner, DriverResponse Driver, DriverResponse PartnerDriver,
        string CashBox, string Bank, Func<string, DriverResponse, string?, Task<TripResponse>> Trip);

    private Task<World> WorldAsync() => WorldAsync(f);

    /// <summary>A company with customer, partner, own and partner drivers, tariffs and rates, and a trip maker (also used by the accounting dashboard tests).</summary>
    internal static async Task<World> WorldAsync(ApiFactory f)
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        await (await c.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var accounts = (await c.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(a => a.Code);
        var box = (await c.GetFromJsonAsync<List<CashBoxResponse>>(U(o, "/accounting/cash-boxes")))!.First();
        var bank = await ReadAsync<BankAccountResponse>(await c.PostAsJsonAsync(U(o, "/accounting/bank-accounts"),
            new BankAccountRequest("Current account", "Cairo Amman Bank", "0131000302", null, null, accounts["110201"].Id, null)));
        var customer = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), new CustomerRequest("Arab Potash", null, null, "0781234567", null, null, null, null, 30, null, null, null, null, null)));
        var partner = await ReadAsync<PartnerResponse>(await c.PostAsJsonAsync(U(o, "/partners"), new PartnerRequest("Al-Noor Transport", null, null, null, null, null, null, null, null, null, null)));
        var material = await ReadAsync<MaterialResponse>(await c.PostAsJsonAsync(U(o, "/materials"), new MaterialRequest("Potash", null, null, null)));
        var loading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/loading-sites"), new SiteRequest("Ghor Al-Safi", null, null, null)));
        var unloading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/unloading-sites"), new SiteRequest("Aqaba Port", null, null, null)));
        var types = (await c.GetFromJsonAsync<List<OwnershipTypeResponse>>(U(o, "/ownership-types")))!;
        var rented = await ReadAsync<OwnershipTypeResponse>(await c.PostAsJsonAsync(U(o, "/ownership-types"), new OwnershipTypeRequest("Rented", "External", null, null)));
        DriverRequest Driver(string name, string nid, string plate, string type, string? partnerId) =>
            new(name, nid, "0791234567", null, null, "L-1", "C", Today.AddYears(1), "Cash", null, null, null, plate, "Volvo", null, 32m, type, partnerId,
                Today.AddYears(1), Today.AddYears(1), Today.AddYears(1), null, null, null, null);
        var own = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), Driver("Ahmad Saleh", "9871234560", "10-11111", types.Single(t => t.IsBuiltIn).Id, null)));
        var ext = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), Driver("Sami Abbadi", "9871234561", "10-22222", rented.Id, partner.Id)));
        await (await c.PostAsJsonAsync(U(o, "/tariffs"), new TariffRequest("Customer", customer.Id, material.Id, null, null, 6.000m, null, null, null, null, null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U(o, "/tariffs"), new TariffRequest("Partner", partner.Id, null, null, null, 2.750m, null, null, null, null, null))).EnsureOkAsync();
        await (await c.PutAsJsonAsync(U(o, $"/monthly-rates/{Today.ToString("yyyy-MM", CultureInfo.InvariantCulture)}"),
            new MonthRatesRequest([new SiteRateRequest(loading.Id, 3.500m, 4.250m, null)]))).EnsureOkAsync();
        async Task<TripResponse> Trip(string receipt, DriverResponse driver, string? partnerId) =>
            (await ReadAsync<TripSaveResult>(await c.PostAsJsonAsync(U(o, "/trips"), new TripRequest(receipt, Today, null, null, driver.Id, customer.Id, loading.Id, unloading.Id,
                material.Id, partnerId, 32.000m, 31.850m, null, null, null, null, null, null, null)))).Trip;
        return new World(o, customer, partner, own, ext, box.Id, bank.Id, Trip);
    }

    private Task<JournalEntry> EntryAsync(string sourceType) =>
        f.DbAsync(db => db.Set<JournalEntry>().IgnoreQueryFilters().Include(e => e.Lines).Where(e => e.SourceType == sourceType).OrderByDescending(e => e.Id).FirstAsync());

    // ------------------------------------------------------------------ receipts (7.4.1)

    [Fact]
    public async Task Receipts_against_invoices_and_on_account_post_void_and_print_FR067_FR071()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var inv = await ReadAsync<InvoiceDetails>(await o.Client.PostAsJsonAsync(U(o, "/invoices"), new InvoiceRequest(w.Customer.Id, "Manual", Today, null, null,
            [new ManualLineRequest("Equipment move", 1, 1000m)], 0, 0, null, null, null, true, null, null)));
        Assert.Equal(1160.000m, inv.Invoice.Total);

        ReceiptRequest Receipt(decimal amount, string? invoice, string method = "Cash", string? reference = null) => new(w.Customer.Id, Today, invoice, amount, method,
            method == "Cash" ? w.CashBox : null, method == "Cash" ? null : w.Bank, reference, 2, null, null);
        Assert.Equal("receipt.exceedsRemaining", await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), Receipt(1200m, inv.Invoice.Id))).ProblemCodeAsync());
        var r1 = await ReadAsync<ReceiptDetails>(await o.Client.PostAsJsonAsync(U(o, "/receipts"), Receipt(400m, inv.Invoice.Id)));
        Assert.StartsWith("RCV-", r1.Receipt.Number, StringComparison.Ordinal);
        var after = await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}")));
        Assert.Equal(760.000m, after.Invoice.RemainingAmount);
        Assert.Equal("PartiallyPaid", after.Invoice.PaymentState);
        var e = await EntryAsync("Receipt");
        Assert.Contains(e.Lines, l => l.Credit == 400m && l.PartyType == "Customer");
        Assert.Equal(400m, e.TotalDebit);

        // Bank transfers need a reference; on account when no invoice.
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/receipts"), Receipt(50m, null, "Bank"))).StatusCode);
        var onAccount = await ReadAsync<ReceiptDetails>(await o.Client.PostAsJsonAsync(U(o, "/receipts"), Receipt(50m, null, "Bank", "TRF-778")));
        Assert.Null(onAccount.Receipt.Invoice);
        Assert.Equal("BankAccount", onAccount.Receipt.Account.Kind);
        var list = await ReadAsync<ReceiptPage>(await o.Client.GetAsync(U(o, "/receipts?applied=account")));
        Assert.Equal(50m, list.Totals.Amount);

        // The invoice cannot be voided with an active receipt (FR-045).
        Assert.Equal("invoice.hasPayments", await (await o.Client.PostAsJsonAsync(U(o, $"/invoices/{inv.Invoice.Id}/void"),
            new InvoiceVoidRequest("x", after.Invoice.RowVersion))).ProblemCodeAsync());

        // Vouchers: number, amount in words; the second print is a copy (FR-070, FR-090).
        var first = await (await o.Client.GetAsync(U(o, $"/receipts/{r1.Receipt.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains(r1.Receipt.Number, first, StringComparison.Ordinal);
        Assert.Contains(r1.AmountInWords, first, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"copy\"", first, StringComparison.Ordinal);
        var second = await (await o.Client.GetAsync(U(o, $"/receipts/{r1.Receipt.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("class=\"copy\"", second, StringComparison.Ordinal);

        // Void: invoice balance restored, reversal, number kept (FR-069).
        var voided = await ReadAsync<ReceiptDetails>(await o.Client.PostAsJsonAsync(U(o, $"/receipts/{r1.Receipt.Id}/void"), new CashVoidRequest("Bounced cheque", r1.Receipt.RowVersion)));
        Assert.Equal("Void", voided.Receipt.Status);
        Assert.Equal(1160.000m, (await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{inv.Invoice.Id}")))).Invoice.RemainingAmount);
        Assert.Equal("receipt.void", await (await o.Client.PostAsJsonAsync(U(o, $"/receipts/{r1.Receipt.Id}/void"), new CashVoidRequest("x", voided.Receipt.RowVersion))).ProblemCodeAsync());
        var export = await o.Client.GetAsync(U(o, "/receipts/export?status=all"));
        await export.EnsureOkAsync();
    }

    // ------------------------------------------------------------------ advances and settlements (7.4.2, 7.4.3, 8.5)

    [Fact]
    public async Task Settlement_reproduces_the_PRD_8_5_worked_example_TST09()
    {
        // PRD 8.5: gross 1,250.000 − advances 400.000 − deductions 35.000 = net 815.000 JOD.
        var w = await WorldAsync();
        var o = w.Owner;
        var c = o.Client;
        var refs = (await ReadAsync<TripDetails>(await c.GetAsync(U(o, $"/trips/{(await w.Trip("REF", w.Driver, null)).Id}")))).Trip;
        await (await c.PostAsJsonAsync(U(o, $"/trips/{refs.Id}/void"), new TripVoidRequest("Reference trip only", refs.RowVersion))).EnsureOkAsync();
        for (var i = 1; i <= 5; i++)   // five trips of 50.000 t at a manual driver rate of 5.000 (masters.manage): 5 × 250.000 = 1,250.000
        {
            var trip = (await ReadAsync<TripSaveResult>(await c.PostAsJsonAsync(U(o, "/trips"), new TripRequest($"EX-{i}", Today, null, null, w.Driver.Id, w.Customer.Id,
                refs.LoadingSite.Id, refs.UnloadingSite.Id, refs.Material.Id, null, 50.000m, 50.000m, null, 5.000m, null, null, null, null, null)))).Trip;
            Assert.Equal(250.000m, trip.DriverWage);
        }
        foreach (var amount in new[] { 300m, 100m })
            await (await c.PostAsJsonAsync(U(o, "/advances"), new AdvanceRequest(w.Driver.Id, Today, amount, "Cash", w.CashBox, null, null, null, null, null))).EnsureOkAsync();

        var stl = await ReadAsync<SettlementDetails>(await c.PostAsJsonAsync(U(o, "/settlements"), new SettlementRequest(w.Driver.Id, Today, null, null,
            [new SettlementDeductionRequest("Fine", 20m, "Late delivery"), new SettlementDeductionRequest("Other", 15m, "Tarpaulin")], "Cash", w.CashBox, null, null, null)));
        Assert.Equal(1250.000m, stl.Settlement.GrossWages);
        Assert.Equal(400.000m, stl.Settlement.AdvancesTotal);
        Assert.Equal(35.000m, stl.Settlement.DeductionsTotal);
        Assert.Equal(815.000m, stl.Settlement.NetPayable);
        // Entry (8.6, accrual on): Dr driver payable 1,250 = Cr advances 400 + Cr deductions 35 + Cr cash 815.
        var e = await EntryAsync("Settlement");
        Assert.Equal(1250.000m, e.TotalDebit);
        Assert.Contains(e.Lines, l => l.Credit == 815.000m);
        Assert.Contains(e.Lines, l => l.Credit == 400.000m);
        Assert.Contains(e.Lines, l => l.Credit == 35.000m);
    }

    [Fact]
    public async Task Advances_and_settlement_per_8_5_lock_items_post_and_void_FR072_FR082()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var t1 = await w.Trip("T-1", w.Driver, null);
        var t2 = await w.Trip("T-2", w.Driver, null);
        Assert.Equal(111.475m, t1.DriverWage);   // 31.850 × 3.500 (PRD 8.2)
        AdvanceRequest Advance(decimal amount, string? rowVersion = null) => new(w.Driver.Id, Today, amount, "Cash", w.CashBox, null, null, "Fuel", null, rowVersion);
        var adv = await ReadAsync<AdvanceDetails>(await o.Client.PostAsJsonAsync(U(o, "/advances"), Advance(100m)));
        Assert.Contains((await EntryAsync("Advance")).Lines, l => l.Debit == 100m && l.PartyType == "Driver");

        // Edit while unsettled: reverse + re-post (FR-073).
        var edited = await ReadAsync<AdvanceDetails>(await o.Client.PutAsJsonAsync(U(o, $"/advances/{adv.Advance.Id}"), Advance(120m, adv.Advance.RowVersion)));
        Assert.Equal(120m, edited.Advance.Amount);
        Assert.NotEqual(adv.Advance.JournalEntry!.Number, edited.Advance.JournalEntry!.Number);   // a new entry; the first one reversed

        // Preview: open items up to the date (FR-076, FR-077).
        var preview = await ReadAsync<SettlementPreview>(await o.Client.GetAsync(U(o, $"/settlements/preview?driver={w.Driver.Id}&date={Today:yyyy-MM-dd}")));
        Assert.Equal(2, preview.Trips.Count);
        Assert.Equal(222.950m, preview.Gross);
        Assert.Equal(120m, preview.AdvancesTotal);
        Assert.Equal(102.950m, preview.Net);

        // A deduction larger than the net is refused (net ≥ 0).
        SettlementRequest Settle(decimal fine, IReadOnlyList<string>? trips = null, IReadOnlyList<string>? advances = null) => new(w.Driver.Id, Today, trips, advances,
            [new SettlementDeductionRequest("Fine", fine, "Late delivery"), new SettlementDeductionRequest("Other", 0, "nothing")], "Cash", w.CashBox, null, null, null);
        Assert.Equal("settlement.netNegative", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(200m))).ProblemCodeAsync());
        var stl = await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(10m)));
        Assert.Equal(222.950m, stl.Settlement.GrossWages);
        Assert.Equal(92.950m, stl.Settlement.NetPayable);   // 222.950 − 120 − 10
        Assert.Single(stl.Deductions);                       // zero lines are not kept
        var e = await EntryAsync("Settlement");
        Assert.Contains(e.Lines, l => l.Debit == 222.950m);  // driver payable (wages accrued at trip save)
        Assert.Contains(e.Lines, l => l.Credit == 120m && l.PartyType == "Driver");
        Assert.Contains(e.Lines, l => l.Credit == 92.950m);
        Assert.Equal(e.TotalDebit, e.TotalCredit);

        // Settled items are locked (BR-031, BR-032).
        var trip = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{t1.Id}")))).Trip;
        Assert.Equal("trip.settled", await (await o.Client.PostAsJsonAsync(U(o, $"/trips/{t1.Id}/void"), new TripVoidRequest("x", trip.RowVersion))).ProblemCodeAsync());
        var advNow = await ReadAsync<AdvanceDetails>(await o.Client.GetAsync(U(o, $"/advances/{adv.Advance.Id}")));
        Assert.Equal("advance.settled", await (await o.Client.PostAsJsonAsync(U(o, $"/advances/{adv.Advance.Id}/void"), new CashVoidRequest("x", advNow.Advance.RowVersion))).ProblemCodeAsync());
        Assert.Equal("settlement.empty", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(0m))).ProblemCodeAsync());

        // Statement and account: settled items net to zero (FR-076, FR-083).
        var account = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Equal(0m, account.Balance);
        Assert.Empty(account.OpenTrips);
        var statement = await ReadAsync<StatementResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/statement?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}")));
        Assert.Equal(222.950m, statement.TotalIncrease);
        Assert.Equal(0m, statement.Closing);
        await (await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/statement/export?format=xlsx"))).EnsureOkAsync();

        // Settlement statement with its payment voucher (FR-082).
        var html = await (await o.Client.GetAsync(U(o, $"/settlements/{stl.Settlement.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("10-11111", html, StringComparison.Ordinal);
        Assert.Contains("T-2", html, StringComparison.Ordinal);
        Assert.Contains("class=\"annex\"", html, StringComparison.Ordinal);

        // Void: items released, reversal (FR-081); a subset can then be settled.
        await (await o.Client.PostAsJsonAsync(U(o, $"/settlements/{stl.Settlement.Id}/void"), new CashVoidRequest("Wrong deduction", stl.Settlement.RowVersion))).EnsureOkAsync();
        var partial = await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(0m, [t2.Id], [])));
        Assert.Equal(111.475m, partial.Settlement.NetPayable);
        Assert.Equal("settlement.itemUnavailable", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(0m, [t2.Id], []))).ProblemCodeAsync());
        var reopened = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Single(reopened.OpenTrips);
        Assert.Equal(111.475m - 120m, reopened.Balance);
    }

    [Fact]
    public async Task Without_wage_accrual_the_settlement_debits_wage_expense_and_shortages_are_suggested_BR031a_FR078()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var accrued = await w.Trip("A-1", w.Driver, null);   // accrual on (default): wage posted to driver payable
        var ops = await ReadAsync<Contracts.Platform.OperationSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/operations")));
        await (await o.Client.PutAsJsonAsync(U(o, "/settings/operations"), new Contracts.Platform.OperationSettingsRequest(ops.BillOnUnloadedWeight, ops.PayDriverOnLoadedWeight,
            ops.ShortageTolerancePct, ops.LockTariffs, ops.InvoiceApproval, false, true))).EnsureOkAsync();
        // A trip with a 1 t shortage (loaded 32, unloaded 31): above the 0.5 % tolerance.
        var c = o.Client;
        var flagged = (await ReadAsync<TripSaveResult>(await c.PostAsJsonAsync(U(o, "/trips"), new TripRequest("A-2", Today, null, null, w.Driver.Id, w.Customer.Id,
            accrued.LoadingSite.Id, accrued.UnloadingSite.Id, accrued.Material.Id, null, 32.000m, 31.000m, null, null, null, null, null, null, null)))).Trip;
        Assert.True(flagged.IsShortageFlagged);
        var preview = await ReadAsync<SettlementPreview>(await c.GetAsync(U(o, $"/settlements/preview?driver={w.Driver.Id}")));
        var suggestion = Assert.Single(preview.Suggestions);
        Assert.Equal(0.840m, suggestion.ExcessWeight);        // 1.000 − 32 × 0.5 %
        Assert.Equal(5.040m, suggestion.Amount);              // 0.840 × 6.000
        var stl = await ReadAsync<SettlementDetails>(await c.PostAsJsonAsync(U(o, "/settlements"), new SettlementRequest(w.Driver.Id, Today, null, null,
            [new SettlementDeductionRequest("Shortage", suggestion.Amount, "A-2")], "Cash", w.CashBox, null, null, null)));
        var e = await EntryAsync("Settlement");
        Assert.Contains(e.Lines, l => l.Debit == accrued.DriverWage);                       // driver payable (accrued trip)
        Assert.Contains(e.Lines, l => l.Debit == flagged.DriverWage);                       // wage expense (not accrued)
        Assert.Contains(e.Lines, l => l.Credit == 5.040m);                                 // driver deductions
        Assert.Equal(stl.Settlement.GrossWages - 5.040m, stl.Settlement.NetPayable);
    }

    // ------------------------------------------------------------------ partner payments (7.4.4)

    [Fact]
    public async Task Partner_payments_are_capped_at_the_balance_due_FR084_FR088()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var trip = await w.Trip("P-1", w.PartnerDriver, w.Partner.Id);
        Assert.True(trip.PartnerCost > 0);
        var account = await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")));
        Assert.Equal(trip.PartnerCost, account.BalanceDue);
        PartnerPaymentRequest Pay(decimal amount) => new(w.Partner.Id, Today, amount, "CliQ", null, w.Bank, "CLIQ-1", null, null);
        var refused = await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(trip.PartnerCost + 0.001m));
        Assert.Equal("partnerPayment.exceedsBalance", await refused.ProblemCodeAsync());
        var paid = await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(trip.PartnerCost)));
        Assert.Contains((await EntryAsync("PartnerPayment")).Lines, l => l.Debit == trip.PartnerCost && l.PartyType == "Partner");
        Assert.Equal(0m, (await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")))).BalanceDue);
        var statement = await ReadAsync<StatementResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/statement")));
        Assert.Equal(2, statement.Lines.Count);
        Assert.Equal(2.750m, statement.Lines.First(l => l.Kind == "Trip").Rate);
        Assert.Equal(0m, statement.Closing);
        // The paid trip cannot be voided now: the partner would be overpaid (review M2).
        var current = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{trip.Id}")))).Trip;
        Assert.Equal("trip.partnerOverpaid", await (await o.Client.PostAsJsonAsync(U(o, $"/trips/{trip.Id}/void"), new TripVoidRequest("x", current.RowVersion))).ProblemCodeAsync());
        var voucher = await (await o.Client.GetAsync(U(o, $"/partner-payments/{paid.Payment.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("CLIQ-1", voucher, StringComparison.Ordinal);
        await (await o.Client.PostAsJsonAsync(U(o, $"/partner-payments/{paid.Payment.Id}/void"), new CashVoidRequest("Duplicate", paid.Payment.RowVersion))).EnsureOkAsync();
        Assert.Equal(trip.PartnerCost, (await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")))).BalanceDue);
    }

    // ------------------------------------------------------------------ permissions and isolation

    [Fact]
    public async Task Permissions_and_isolation_for_cash_documents()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var adv = await ReadAsync<AdvanceDetails>(await o.Client.PostAsJsonAsync(U(o, "/advances"), new AdvanceRequest(w.Driver.Id, Today, 50m, "Cash", w.CashBox, null, null, null, null, null)));
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        var accountant = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Accountant);
        await (await viewer.Client.GetAsync(U(o, "/receipts"))).EnsureOkAsync();                       // inv.view reads receipts
        await (await dispatcher.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account"))).EnsureOkAsync();   // drv.view
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(o, "/receipts"),
            new ReceiptRequest(w.Customer.Id, Today, null, 10m, "Cash", w.CashBox, null, null, 0, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.PostAsJsonAsync(U(o, "/advances"),
            new AdvanceRequest(w.Driver.Id, Today, 10m, "Cash", w.CashBox, null, null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.GetAsync(U(o, "/receipts"))).StatusCode);
        await (await accountant.Client.PostAsJsonAsync(U(o, "/receipts"), new ReceiptRequest(w.Customer.Id, Today, null, 10m, "Cash", w.CashBox, null, null, 0, null, null))).EnsureOkAsync();
        // Idempotent create by client id (API-05).
        var clientId = Guid.NewGuid().ToString();
        var a1 = await o.Client.PostAsJsonAsync(U(o, "/advances"), new AdvanceRequest(w.Driver.Id, Today, 5m, "Cash", w.CashBox, null, null, null, clientId, null));
        var a2 = await o.Client.PostAsJsonAsync(U(o, "/advances"), new AdvanceRequest(w.Driver.Id, Today, 5m, "Cash", w.CashBox, null, null, null, clientId, null));
        Assert.Equal(HttpStatusCode.Created, a1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, a2.StatusCode);
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/advances/{adv.Advance.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/drivers/{w.Driver.Id}/account"))).StatusCode);
    }

    // ------------------------------------------------------------------ receipts against several invoices (revision 6, item 4)

    private static async Task<InvoiceResponse> ManualInvoiceAsync(World w, decimal net, DateOnly date) =>
        (await ReadAsync<InvoiceDetails>(await w.Owner.Client.PostAsJsonAsync(U(w.Owner, "/invoices"), new InvoiceRequest(w.Customer.Id, "Manual", date, null, null,
            [new ManualLineRequest("Service", 1, net)], 0, 0, null, null, null, true, null, null)))).Invoice;

    private static ReceiptRequest Allocated(World w, decimal amount, params (string Invoice, decimal Amount)[] allocations) =>
        new(w.Customer.Id, Today, null, amount, "Cash", w.CashBox, null, null, 0, null, null, allocations.Select(a => new ReceiptAllocationRequest(a.Invoice, a.Amount)).ToList());

    [Fact]
    public async Task A_receipt_is_applied_to_several_invoices_with_a_part_payment_and_the_rest_on_account()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var i1 = await ManualInvoiceAsync(w, 100m, Today.AddDays(-2));   // 116.000
        var i2 = await ManualInvoiceAsync(w, 200m, Today.AddDays(-1));   // 232.000
        var i3 = await ManualInvoiceAsync(w, 300m, Today);               // 348.000

        // 500: invoice 1 and 2 in full, 100 on invoice 3 (part payment), 52 on account.
        var r = await ReadAsync<ReceiptDetails>(await o.Client.PostAsJsonAsync(U(o, "/receipts"), Allocated(w, 500m, (i1.Id, 116m), (i2.Id, 232m), (i3.Id, 100m))));
        Assert.Equal([116m, 232m, 100m], r.Receipt.Allocations!.Select(a => a.Amount));
        Assert.Equal(52m, r.Receipt.Unallocated);
        Assert.Null(r.Receipt.Invoice);   // several invoices: no single invoice link
        var states = new List<(decimal Remaining, string State)>();
        foreach (var id in new[] { i1.Id, i2.Id, i3.Id })
        {
            var inv = (await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{id}")))).Invoice;
            states.Add((inv.RemainingAmount, inv.PaymentState));
        }
        Assert.Equal([(0m, "Closed"), (0m, "Closed"), (248m, "PartiallyPaid")], states);

        // The entry credits trade receivables with the whole amount and names the invoices.
        var e = await EntryAsync("Receipt");
        Assert.Equal(500m, e.TotalDebit);
        Assert.Contains(i1.Number!, e.Description, StringComparison.Ordinal);

        // Ageing: 248 open on invoice 3, 52 unapplied.
        var ageing = await ReadAsync<Contracts.ApAr.AgeingReport>(await o.Client.GetAsync(U(o, "/apar/ageing/receivables")));
        var row = ageing.Rows.Single(x => x.Party.Name == w.Customer.Name);
        Assert.Equal((248m, 52m), (row.Amounts.Total, row.Amounts.Unapplied));

        // Lists: filter by any of its invoices, "applied" = to invoices.
        var byInvoice = await ReadAsync<ReceiptPage>(await o.Client.GetAsync(U(o, $"/receipts?invoice={i2.Id}")));
        Assert.Equal([r.Receipt.Id], byInvoice.Page.Items.Select(x => x.Id));
        Assert.Single((await ReadAsync<ReceiptPage>(await o.Client.GetAsync(U(o, "/receipts?applied=invoice")))).Page.Items);

        // Void gives every invoice its amount back.
        await (await o.Client.PostAsJsonAsync(U(o, $"/receipts/{r.Receipt.Id}/void"), new CashVoidRequest("Bounced", r.Receipt.RowVersion))).EnsureOkAsync();
        foreach (var (id, total) in new[] { (i1.Id, 116m), (i2.Id, 232m), (i3.Id, 348m) })
            Assert.Equal(total, (await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{id}")))).Invoice.RemainingAmount);
    }

    [Fact]
    public async Task Allocations_cannot_exceed_an_invoice_balance_or_the_receipt_amount()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var i1 = await ManualInvoiceAsync(w, 100m, Today);   // 116.000
        var i2 = await ManualInvoiceAsync(w, 200m, Today);   // 232.000
        Assert.Equal("receipt.exceedsRemaining", await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), Allocated(w, 500m, (i1.Id, 120m)))).ErrorCodeAsync());
        Assert.Equal("receipt.allocationsExceedAmount", await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), Allocated(w, 300m, (i1.Id, 116m), (i2.Id, 200m)))).ErrorCodeAsync());
        Assert.Equal("receipt.allocationDuplicate", await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), Allocated(w, 300m, (i1.Id, 10m), (i1.Id, 10m)))).ErrorCodeAsync());
        Assert.Equal("validation.range", await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), Allocated(w, 300m, (i1.Id, 0m)))).ErrorCodeAsync());

        // Another customer's invoice is not open for this customer.
        var other = await ReadAsync<CustomerResponse>(await o.Client.PostAsJsonAsync(U(o, "/customers"), new CustomerRequest("Other Co", null, null, "0781234500", null, null, null, null, 30, null, null, null, null, null)));
        var foreign = await ManualInvoiceAsync(w with { Customer = other }, 50m, Today);
        Assert.Equal("receipt.invoiceNotOpen", await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), Allocated(w, 50m, (foreign.Id, 10m)))).ErrorCodeAsync());

        // Nothing was saved by the refused receipts.
        Assert.Equal(116m, (await ReadAsync<InvoiceDetails>(await o.Client.GetAsync(U(o, $"/invoices/{i1.Id}")))).Invoice.RemainingAmount);
        Assert.Equal(0, (await ReadAsync<ReceiptPage>(await o.Client.GetAsync(U(o, "/receipts")))).Page.TotalCount);
    }
}
