using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Shared.Contracts;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner findings of 27 Oct 2026. DT-276: a driver's balance from manual / opening journal entries is a line of its own in the settlement —
/// deducted like an advance (repaid or carried) or added to the net —, a settlement may settle only that balance, its entry clears it on the same
/// account with the driver, and RPT-13, RPT-19 and RPT-23 show 0 afterwards; voiding the settlement restores it. DT-277: the same for partners
/// with the next payment or a repayment, and the trip coverage (DT-271) counts only what was paid for trips. DT-278: dashboard figures split
/// into "What we owe" and "Owed to us" without negatives; RPT-19 / RPT-20 by side. DT-279: one "Cancelled" journal view with the reason and the
/// links both ways.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PreviousBalanceTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<Dictionary<string, AccountResponse>> AccountsAsync(SignedInUser o) =>
        (await o.Client.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(a => a.Code);

    /// <summary>A manual entry naming the party on <paramref name="code"/>: debit = the party owes the company, credit = owed to the party.</summary>
    private static async Task<JournalEntryResponse> ManualAsync(SignedInUser o, Dictionary<string, AccountResponse> a, string code, string partyType, string partyId,
        decimal debit, decimal credit, string description = "Opening balance of the party") =>
        await ReadAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"), new ManualEntryRequest(Today, description,
            [new JournalLineRequest(a[code].Id, debit, credit, null, new PartyRequest(partyType, partyId), null),
             new JournalLineRequest(a["110101"].Id, credit, debit, null, null, null)], null)));

    private static decimal Num(object? v) => v is JsonElement e && e.ValueKind == JsonValueKind.Number ? e.GetDecimal() : throw new InvalidOperationException($"not a number: {v}");

    private static async Task<(decimal Owed, decimal Owes)?> BalanceRowAsync(SignedInUser o, string report, string name, string? side = null)
    {
        var r = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, $"/reports/standard/{report}?asOf={D(Today)}" + (side is null ? "" : "&side=" + side))));
        var owed = r.Columns.ToList().FindIndex(c => c.Key == "owed");
        var owes = r.Columns.ToList().FindIndex(c => c.Key == "owes");
        var row = r.Rows.SingleOrDefault(x => x.Values[0] is JsonElement e && e.GetString() == name);
        return row is null ? null : (Num(row.Values[owed]), Num(row.Values[owes]));
    }

    private static async Task<ReconciliationRow> ReconciliationAsync(SignedInUser o, string code) =>
        (await ReadAsync<ReconciliationReport>(await o.Client.GetAsync(U(o, $"/accounting/reports/reconciliation?asOf={D(Today)}")))).Rows.Single(r => r.Account.Code == code);

    private static async Task<decimal> DriverClosingAsync(SignedInUser o, string driver) =>
        (await ReadAsync<StatementResponse>(await o.Client.GetAsync(U(o, $"/drivers/{driver}/statement?to={D(Today)}")))).Closing;

    private static async Task<decimal> PartnerClosingAsync(SignedInUser o, string partner) =>
        (await ReadAsync<StatementResponse>(await o.Client.GetAsync(U(o, $"/partners/{partner}/statement?to={D(Today)}")))).Closing;

    // ------------------------------------------------------------------ drivers (DT-276)

    [Fact]
    public async Task Driver_with_only_a_manual_balance_settles_it_without_trips_and_every_report_shows_zero_until_the_settlement_is_voided()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var a = await AccountsAsync(o);
        // The demo case: a manual entry put 100 on the driver in driver advances (1104) — the driver owes the company 100.
        var jv = await ManualAsync(o, a, "1104", "Driver", w.Driver.Id, 100, 0, "Opening driver advance");

        var account = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Equal((-100m, -100m, 0), (account.PreviousBalance, account.NetDue, account.OpenTrips.Count));
        var listed = Assert.Single(account.PreviousBalanceEntries!);
        Assert.Equal((jv.Number, -100m, jv.Id), (listed.Entry.Number, listed.Amount, listed.Entry.Id));
        var preview = await ReadAsync<SettlementPreview>(await o.Client.GetAsync(U(o, $"/settlements/preview?driver={w.Driver.Id}&date={D(Today)}")));
        Assert.Equal((-100m, -100m), (preview.PreviousBalance, preview.Net));
        Assert.Equal((0m, 100m), (await BalanceRowAsync(o, "driver-balances", w.Driver.FullName))!.Value);   // DT-278: no negative, "Owes the company"
        Assert.Null(await BalanceRowAsync(o, "driver-balances", w.Driver.FullName, "owed"));
        Assert.NotNull(await BalanceRowAsync(o, "driver-balances", w.Driver.FullName, "owes"));

        SettlementRequest Settle(decimal? repaid, bool? include = null) =>
            new(w.Driver.Id, Today, null, null, [], "Cash", w.CashBox, null, null, null, repaid, include);
        // Without the previous balance nothing is open; with it the driver must repay (or carry) the 100.
        Assert.Equal("settlement.empty", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(null, false))).ProblemCodeAsync());
        Assert.Equal("settlement.netNegative", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(null))).ProblemCodeAsync());
        var stl = await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(100)));
        Assert.Equal((-100m, 100m, 0m, 0m, 0), (stl.Settlement.PreviousBalance, stl.Settlement.RepaidAmount, stl.Settlement.CarriedForward, stl.Settlement.NetPayable, stl.Trips.Count));
        Assert.Equal(jv.Number, Assert.Single(stl.PreviousBalanceEntries!).Entry.Number);
        // Its entry clears 1104 with the driver (Cr 100) and takes the cash (Dr 100).
        var entry = await ReadAsync<JournalEntryResponse>(await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{stl.Settlement.JournalEntry!.Id}")));
        var clearing = Assert.Single(entry.Lines, l => l.Account.Code == "1104");
        Assert.Equal((0m, 100m, "Driver", w.Driver.Id), (clearing.Debit, clearing.Credit, clearing.Party?.Type, clearing.Party?.Id));

        // RPT-13, RPT-19 and RPT-23 all show 0; the account has nothing open.
        Assert.Equal(0m, await DriverClosingAsync(o, w.Driver.Id));
        Assert.Equal((0m, 0m), (await BalanceRowAsync(o, "driver-balances", w.Driver.FullName))!.Value);
        var rec = await ReconciliationAsync(o, "1104");
        Assert.Equal((0m, 0m, 0m, 0m, 0m, 0m), (rec.LedgerBalance, rec.PartyBalances, rec.Difference, rec.DocumentDifference ?? -1, rec.ManualAmount, rec.Unexplained));
        Assert.Empty(rec.GapEntries!);
        account = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Equal((0m, 0m), (account.PreviousBalance, account.NetDue));
        Assert.Equal("settlement.empty", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(null))).ProblemCodeAsync());

        // Voiding the settlement restores the balance everywhere.
        await (await o.Client.PostAsJsonAsync(U(o, $"/settlements/{stl.Settlement.Id}/void"), new CashVoidRequest("Wrong date", stl.Settlement.RowVersion))).EnsureOkAsync();
        account = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Equal(-100m, account.PreviousBalance);
        Assert.Equal(-100m, await DriverClosingAsync(o, w.Driver.Id));
        Assert.Equal((0m, 100m), (await BalanceRowAsync(o, "driver-balances", w.Driver.FullName))!.Value);
        rec = await ReconciliationAsync(o, "1104");
        Assert.Equal((100m, 100m, 0m, 100m), (rec.LedgerBalance, rec.PartyBalances, rec.Difference, rec.ManualAmount));
    }

    [Fact]
    public async Task Driver_balance_owed_by_the_company_is_paid_and_with_trips_the_previous_balance_is_deducted_or_carried()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var a = await AccountsAsync(o);
        // Owed by the company: a manual credit to driver payable (2102) with the driver — paid with a settlement without trips.
        await ManualAsync(o, a, "2102", "Driver", w.Driver.Id, 0, 40, "Wage owed before the system");
        var paid = await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"),
            new SettlementRequest(w.Driver.Id, Today, null, null, [], "Cash", w.CashBox, null, null, null)));
        Assert.Equal((40m, 40m), (paid.Settlement.PreviousBalance, paid.Settlement.NetPayable));
        var entry = await ReadAsync<JournalEntryResponse>(await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{paid.Settlement.JournalEntry!.Id}")));
        Assert.Equal(40m, Assert.Single(entry.Lines, l => l.Account.Code == "2102").Debit);
        Assert.Equal(0m, await DriverClosingAsync(o, w.Driver.Id));
        Assert.Equal((0m, 0m, 0m), ((await ReconciliationAsync(o, "2102")).LedgerBalance, (await ReconciliationAsync(o, "2102")).Difference,
            (await ReconciliationAsync(o, "2102")).Unexplained));

        // Trips plus a manual balance the driver owes: deducted from the wages like an advance.
        var trip = await w.Trip("PB-1", w.Driver, null);
        await ManualAsync(o, a, "1104", "Driver", w.Driver.Id, 30, 0);
        var net = await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"),
            new SettlementRequest(w.Driver.Id, Today, null, null, [], "Cash", w.CashBox, null, null, null)));
        Assert.Equal((trip.DriverWage, -30m, trip.DriverWage - 30m), (net.Settlement.GrossWages, net.Settlement.PreviousBalance, net.Settlement.NetPayable));
        Assert.Equal(0m, await DriverClosingAsync(o, w.Driver.Id));

        // More owed than the wages: part repaid now, the rest carried as an open advance to the next settlement (DT-228 rules).
        var trip2 = await w.Trip("PB-2", w.Driver, null);
        var owed = trip2.DriverWage + 50m;
        await ManualAsync(o, a, "1104", "Driver", w.Driver.Id, owed, 0);
        var carried = await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"),
            new SettlementRequest(w.Driver.Id, Today, null, null, [], "Cash", w.CashBox, null, null, null, 20m)));
        Assert.Equal((-owed, 20m, 30m), (carried.Settlement.PreviousBalance, carried.Settlement.RepaidAmount, carried.Settlement.CarriedForward));
        Assert.NotNull(carried.Settlement.CarriedAdvance);
        var account = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Equal((0m, 30m, -30m), (account.PreviousBalance, Assert.Single(account.OpenAdvances).Amount, account.NetDue));
        Assert.Equal(-30m, await DriverClosingAsync(o, w.Driver.Id));
        Assert.Equal((0m, 30m), (await BalanceRowAsync(o, "driver-balances", w.Driver.FullName))!.Value);
        var rec = await ReconciliationAsync(o, "1104");
        Assert.Equal((30m, 0m, 0m, 0m), (rec.LedgerBalance, rec.Difference, rec.DocumentDifference ?? -1, rec.Unexplained));   // the carried advance is a document
    }

    // ------------------------------------------------------------------ partners (DT-277)

    [Fact]
    public async Task Partner_manual_balance_is_set_off_on_the_next_payment_and_trip_coverage_counts_only_the_payment_for_trips()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var a = await AccountsAsync(o);
        var t1 = await w.Trip("PP-1", w.PartnerDriver, w.Partner.Id);
        var t2 = await w.Trip("PP-2", w.PartnerDriver, w.Partner.Id);
        var cost = t1.PartnerCost + t2.PartnerCost;
        // The partner owes the company 25 from a manual entry on partner payable (2103).
        var jv = await ManualAsync(o, a, "2103", "Partner", w.Partner.Id, 25, 0);
        var account = await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")));
        Assert.Equal((cost, 0m, -25m, cost - 25m), (account.Cost, account.Paid, account.PreviousBalance, account.BalanceDue));
        Assert.Equal(jv.Number, Assert.Single(account.PreviousBalanceEntries!).Entry.Number);

        PartnerPaymentRequest Pay(decimal amount, bool? include = null, string? kind = null) =>
            new(w.Partner.Id, Today, amount, "Cash", w.CashBox, null, null, null, null, kind, include);
        // Paying the whole trip cost in cash would ignore the 25 the partner owes: refused, the most is cost − 25.
        Assert.Equal("partnerPayment.exceedsBalance", await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(cost))).ProblemCodeAsync());
        var p = await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(cost - 25m)));
        Assert.Equal((cost - 25m, -25m), (p.Payment.Amount, p.Payment.PreviousBalance));
        Assert.Equal(jv.Number, Assert.Single(p.PreviousBalanceEntries!).Entry.Number);
        // Both trips are paid: the payment paid cost − 25 in cash and set off the 25 — together the trips' cost (DT-271 coverage).
        foreach (var t in new[] { t1, t2 })
            Assert.Equal("PartnerPaid", (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{t.Id}")))).Trip.Payment);
        // Entry: Dr partner payable cost (trips), Cr partner payable 25 (the manual amount cleared on the same account), Cr cash cost − 25.
        var entry = await ReadAsync<JournalEntryResponse>(await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{p.Payment.JournalEntry!.Id}")));
        Assert.Contains(entry.Lines, l => l.Account.Code == "2103" && l.Debit == cost);
        Assert.Contains(entry.Lines, l => l.Account.Code == "2103" && l.Credit == 25m && l.Party?.Id == w.Partner.Id);
        // RPT-14, RPT-20 and RPT-23 show 0.
        Assert.Equal(0m, await PartnerClosingAsync(o, w.Partner.Id));
        Assert.Equal((0m, 0m), (await BalanceRowAsync(o, "partner-balances", w.Partner.Name))!.Value);
        var rec = await ReconciliationAsync(o, "2103");
        Assert.Equal((0m, 0m, 0m, 0m, 0m), (rec.LedgerBalance, rec.Difference, rec.DocumentDifference ?? -1, rec.ManualAmount, rec.Unexplained));
        account = await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")));
        Assert.Equal((cost, 0m, 0m), (account.Paid, account.PreviousBalance, account.BalanceDue));

        // Voiding the payment: the trips are unpaid and the 25 is open again.
        await (await o.Client.PostAsJsonAsync(U(o, $"/partner-payments/{p.Payment.Id}/void"), new CashVoidRequest("Wrong amount", p.Payment.RowVersion))).EnsureOkAsync();
        Assert.Equal("PartnerUnpaid", (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{t1.Id}")))).Trip.Payment);
        account = await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")));
        Assert.Equal((-25m, cost - 25m), (account.PreviousBalance, account.BalanceDue));

        // A payment of 0 only sets off the 25 against the oldest trip: that trip is partly paid, nothing paid for the other.
        var offset = await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(0)));
        Assert.Equal((0m, -25m), (offset.Payment.Amount, offset.Payment.PreviousBalance));
        var first = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{t1.Id}")))).Trip;
        Assert.Equal(("PartnerPartlyPaid", t1.PartnerCost - 25m), (first.Payment, first.PartnerOpen));
        Assert.Equal(cost - 25m, await PartnerClosingAsync(o, w.Partner.Id));
        Assert.Equal("validation.failed", await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(0))).ProblemCodeAsync());   // nothing left to set off
    }

    [Fact]
    public async Task Partner_previous_balance_is_repaid_in_parts_or_paid_first_and_never_changes_what_trips_were_paid()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var a = await AccountsAsync(o);
        // The partner owes 100 from a manual entry and has no trips: repaid in two parts (oldest entry first, the last one partly).
        await ManualAsync(o, a, "2103", "Partner", w.Partner.Id, 60, 0, "First");
        await ManualAsync(o, a, "2103", "Partner", w.Partner.Id, 40, 0, "Second");
        PartnerPaymentRequest Pay(decimal amount, string? kind = null) => new(w.Partner.Id, Today, amount, "Cash", w.CashBox, null, null, null, null, kind);
        Assert.Equal("partnerPayment.repaymentExceedsOwed", await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(101, "Repayment"))).ProblemCodeAsync());
        var r1 = await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(70, "Repayment")));
        Assert.Equal((-70m, 2), (r1.Payment.PreviousBalance, r1.PreviousBalanceEntries!.Count));
        var account = await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")));
        Assert.Equal((-30m, -30m, 0m), (account.PreviousBalance, account.BalanceDue, account.Paid));
        await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(30, "Repayment"))).EnsureOkAsync();
        Assert.Equal(0m, await PartnerClosingAsync(o, w.Partner.Id));
        var rec = await ReconciliationAsync(o, "2103");
        Assert.Equal((0m, 0m, 0m, 0m), (rec.LedgerBalance, rec.Difference, rec.DocumentDifference ?? -1, rec.Unexplained));

        // Owed to the partner from a manual entry: the next payment pays it first; the rest pays trips (here one trip, fully).
        var t = await w.Trip("PQ-1", w.PartnerDriver, w.Partner.Id);
        await ManualAsync(o, a, "2103", "Partner", w.Partner.Id, 0, 15);
        var p = await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Pay(t.PartnerCost + 15m)));
        Assert.Equal(15m, p.Payment.PreviousBalance);
        Assert.Equal("PartnerPaid", (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{t.Id}")))).Trip.Payment);
        Assert.Equal(0m, await PartnerClosingAsync(o, w.Partner.Id));
        Assert.Equal((0m, 0m), (await BalanceRowAsync(o, "partner-balances", w.Partner.Name))!.Value);
        // The partner-shipments report's balance due is about trips only.
        rec = await ReconciliationAsync(o, "2103");
        Assert.Equal((0m, 0m, 0m), (rec.LedgerBalance, rec.Difference, rec.Unexplained));
    }

    // ------------------------------------------------------------------ dashboard (DT-278)

    [Fact]
    public async Task Dashboard_splits_what_we_owe_from_what_is_owed_to_us_without_negatives()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var a = await AccountsAsync(o);
        var trip = await w.Trip("DB-1", w.Driver, null);                   // the company owes the driver the wage
        await ManualAsync(o, a, "2103", "Partner", w.Partner.Id, 100, 0);  // the partner owes the company 100
        var d = await ReadAsync<AccountingDashboardResponse>(await o.Client.GetAsync(U(o, "/accounting/dashboard")));
        var owe = d.Figures!.Payables!;
        var ours = d.Figures.OwedToUs!;
        Assert.Equal((trip.DriverWage, 0m), (owe.Drivers, owe.Partners));
        Assert.Equal((0m, 100m, 0m, 100m), (ours.Drivers, ours.Partners, ours.Suppliers, ours.Total));
        Assert.True(owe.Total >= 0 && owe.Suppliers >= 0);
        // RPT-20 by side, as the dashboard links.
        Assert.NotNull(await BalanceRowAsync(o, "partner-balances", w.Partner.Name, "owes"));
        Assert.Null(await BalanceRowAsync(o, "partner-balances", w.Partner.Name, "owed"));
        Assert.Equal("validation.failed", await (await o.Client.GetAsync(U(o, "/reports/standard/partner-balances?side=nobody"))).ProblemCodeAsync());
    }

    // ------------------------------------------------------------------ journal "Cancelled" view (DT-279)

    [Fact]
    public async Task Cancelled_view_lists_voided_manual_entries_voided_documents_and_edits_with_reasons_and_links_both_ways()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var a = await AccountsAsync(o);
        // A voided manual entry.
        var typo = await ManualAsync(o, a, "1104", "Driver", w.Driver.Id, 12, 0, "Typo");
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-entries/{typo.Id}/void"), new VoidRequest("Entered twice", typo.RowVersion))).EnsureOkAsync();
        // An edited manual entry (reversed and replaced).
        var edited = await ManualAsync(o, a, "1104", "Driver", w.Driver.Id, 5, 0, "Before the edit");
        await (await o.Client.PutAsJsonAsync(U(o, $"/accounting/journal-entries/{edited.Id}"), new ManualEntryRequest(Today, "After the edit",
            [new JournalLineRequest(a["1104"].Id, 6, 0, null, new PartyRequest("Driver", w.Driver.Id), null), new JournalLineRequest(a["110101"].Id, 0, 6, null, null, null)],
            edited.RowVersion))).EnsureOkAsync();
        // A voided document (an advance).
        var adv = await ReadAsync<AdvanceDetails>(await o.Client.PostAsJsonAsync(U(o, "/advances"),
            new AdvanceRequest(w.Driver.Id, Today, 7m, "Cash", w.CashBox, null, null, null, null, null)));
        await (await o.Client.PostAsJsonAsync(U(o, $"/advances/{adv.Advance.Id}/void"), new CashVoidRequest("Driver did not take it", adv.Advance.RowVersion))).EnsureOkAsync();

        var cancelled = await ReadAsync<PagedResult<JournalEntrySummary>>(await o.Client.GetAsync(U(o, "/accounting/journal-entries?status=Cancelled")));
        Assert.Equal(3, cancelled.TotalCount);
        Assert.All(cancelled.Items, e => Assert.NotNull(e.ReversedByEntryNumber));
        var voidRow = cancelled.Items.Single(e => e.Number == typo.Number);
        Assert.Equal(("Void", "Entered twice"), (voidRow.Status, voidRow.CancelReason));
        Assert.Equal("Reversed", cancelled.Items.Single(e => e.Number == edited.Number).Status);
        Assert.StartsWith("Entry edited", cancelled.Items.Single(e => e.Number == edited.Number).CancelReason, StringComparison.Ordinal);
        var advRow = cancelled.Items.Single(e => e.SourceType == "Advance");
        Assert.Equal(("Reversed", "Document voided: Driver did not take it"), (advRow.Status, advRow.CancelReason));
        // Manual / automatic split and search follow the same rules; the reversals are under All as "Reversal of …".
        Assert.Equal(2, (await ReadAsync<PagedResult<JournalEntrySummary>>(await o.Client.GetAsync(U(o, "/accounting/journal-entries?status=Cancelled&manual=true")))).TotalCount);
        Assert.Equal(1, (await ReadAsync<PagedResult<JournalEntrySummary>>(await o.Client.GetAsync(U(o, "/accounting/journal-entries?status=Cancelled&manual=false")))).TotalCount);
        Assert.Equal(typo.Number, Assert.Single((await ReadAsync<PagedResult<JournalEntrySummary>>(
            await o.Client.GetAsync(U(o, $"/accounting/journal-entries?status=Cancelled&search={typo.Number}")))).Items).Number);
        var all = await ReadAsync<PagedResult<JournalEntrySummary>>(await o.Client.GetAsync(U(o, "/accounting/journal-entries?pageSize=100")));
        var reversal = all.Items.Single(e => e.ReversesEntryNumber == typo.Number);
        Assert.Equal((voidRow.ReversedByEntryNumber, "Posted"), (reversal.Number, reversal.Status));
        Assert.Contains((await ReadAsync<PagedResult<JournalEntrySummary>>(await o.Client.GetAsync(U(o, "/accounting/journal-entries?manual=false&pageSize=100")))).Items,
            e => e.ReversesEntryNumber == advRow.Number);
        // Links both ways on the entries themselves, with the reason.
        var original = await ReadAsync<JournalEntryResponse>(await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{typo.Id}")));
        Assert.Equal((reversal.Number, "Entered twice"), (original.ReversedByEntry!.Number, original.CancelReason));
        var back = await ReadAsync<JournalEntryResponse>(await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{original.ReversedByEntry.Id}")));
        Assert.Equal(typo.Number, back.ReversesEntry!.Number);
        // The export follows the filter and carries the cancellation and its reason.
        var csv = Encoding.UTF8.GetString(await (await o.Client.GetAsync(U(o, "/accounting/journal-entries/export?status=Cancelled&format=csv"))).Content.ReadAsByteArrayAsync());
        Assert.Contains("Cancelled by " + reversal.Number, csv, StringComparison.Ordinal);
        Assert.Contains("Document voided: Driver did not take it", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("Reversal of", csv, StringComparison.Ordinal);
        Assert.Equal("validation.failed", await (await o.Client.GetAsync(U(o, "/accounting/journal-entries?status=Gone"))).ProblemCodeAsync());
    }
}
