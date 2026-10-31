using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Accounting.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner testing 21 Oct 2026, item 1 (DT-228): a settlement whose advances and deductions exceed the wages is closed by the driver's repayment
/// (cash, bank transfer, CliQ; reference required for the last two), in full or in part — the rest becomes a new open "Carried" advance for
/// the next settlement. One STL entry: Dr cash / bank, Cr driver advances (what leaves them). Void reverses it and voids the carried advance.
/// Partners who owe the company (a paid trip voided) repay by a partner repayment: Dr cash / bank, Cr partner payable.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SettlementRepaymentTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    /// <summary>The journal entry a document points to (by its token), with its lines, and whether a reversal of it exists.</summary>
    private Task<(JournalEntry Entry, bool Reversed)> EntryAsync(DocRef entry) => f.DbAsync(async db =>
    {
        var pid = f.Services.GetRequiredService<IUrlTokenService>().Decode<JournalEntry>(entry.Id)!.Value;
        var e = await db.Set<JournalEntry>().IgnoreQueryFilters().Include(x => x.Lines).SingleAsync(x => x.PublicId == pid);
        return (e, await db.Set<JournalEntry>().IgnoreQueryFilters().AnyAsync(x => x.ReversesEntryId == e.Id));
    });

    private static async Task<AdvanceDetails> AdvanceAsync(CashTests.World w, decimal amount) =>
        await ReadAsync<AdvanceDetails>(await w.Owner.Client.PostAsJsonAsync(U(w.Owner, "/advances"),
            new AdvanceRequest(w.Driver.Id, Today, amount, "Cash", w.CashBox, null, null, "Advance", null, null)));

    private static SettlementRequest Settle(CashTests.World w, decimal? repaid, string method = "Cash", string? reference = null, decimal fine = 0,
        IReadOnlyList<string>? trips = null, IReadOnlyList<string>? advances = null) =>
        new(w.Driver.Id, Today, trips, advances, fine > 0 ? [new SettlementDeductionRequest("Fine", fine, "Damage")] : [], method,
            method == "Cash" ? w.CashBox : null, method == "Cash" ? null : w.Bank, reference, null, repaid);

    [Fact]
    public async Task Full_repayment_in_cash_closes_the_advance_and_void_reverses_it()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var adv = await AdvanceAsync(w, 20m);   // the owner's case: an open advance of 20.000 and no trips

        // Without a repayment the negative net is refused; above the shortfall or zero is refused on the field.
        Assert.Equal("settlement.netNegative", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, null))).ProblemCodeAsync());
        Assert.Equal("settlement.repaymentRange", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, 20.001m))).ProblemCodeAsync());
        Assert.Equal("settlement.repaymentRange", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, 0m))).ProblemCodeAsync());

        var stl = (await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, 20m)))).Settlement;
        Assert.Equal((0m, 20m, 0m), (stl.NetPayable, stl.RepaidAmount, stl.CarriedForward));
        Assert.Null(stl.CarriedAdvance);
        var (e, _) = await EntryAsync(stl.JournalEntry!);
        Assert.Contains(e.Lines, l => l.Credit == 20m && l.PartyType == "Driver");   // Cr driver advances
        Assert.Contains(e.Lines, l => l.Debit == 20m && l.PartyType == null);         // Dr the cash box
        Assert.Equal(2, e.Lines.Count);
        Assert.Equal(e.TotalDebit, e.TotalCredit);

        var account = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Equal(0m, account.Balance);
        Assert.Empty(account.OpenAdvances);
        var statement = await ReadAsync<StatementResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/statement?from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}")));
        var repaidLine = Assert.Single(statement.Lines, l => l.Kind == "Repaid");
        Assert.Equal(20m, repaidLine.Increase);
        Assert.Equal(0m, statement.Closing);
        var html = await (await o.Client.GetAsync(U(o, $"/settlements/{stl.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("class=\"annex\"", html, StringComparison.Ordinal);   // the receipt voucher for the repayment
        Assert.Contains("20.000", html, StringComparison.Ordinal);

        // Void: reversed, the advance is open again.
        await (await o.Client.PostAsJsonAsync(U(o, $"/settlements/{stl.Id}/void"), new CashVoidRequest("Wrong box", stl.RowVersion))).EnsureOkAsync();
        Assert.True((await EntryAsync(stl.JournalEntry!)).Reversed);
        Assert.Null((await ReadAsync<AdvanceDetails>(await o.Client.GetAsync(U(o, $"/advances/{adv.Advance.Id}")))).Advance.Settlement);
        Assert.Equal(-20m, (await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")))).Balance);
    }

    [Fact]
    public async Task Partial_repayment_by_CliQ_carries_the_rest_to_the_next_settlement_and_void_order_is_kept()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var t1 = await w.Trip("R-1", w.Driver, null);                // wage 111.475
        var adv = await AdvanceAsync(w, 200m);                        // net −88.525

        var noRef = await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, 50m, "CliQ"));
        Assert.Equal(HttpStatusCode.BadRequest, noRef.StatusCode);
        Assert.Contains("reference", await noRef.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var stl = (await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, 50m, "CliQ", "CLIQ-7788")))).Settlement;
        Assert.Equal((0m, 50m, 38.525m, "CliQ", "CLIQ-7788"), (stl.NetPayable, stl.RepaidAmount, stl.CarriedForward, stl.PaymentMethod, stl.Reference));
        var (e, _) = await EntryAsync(stl.JournalEntry!);
        Assert.Contains(e.Lines, l => l.Debit == 111.475m && l.PartyType == "Driver");   // driver payable (accrued)
        Assert.Contains(e.Lines, l => l.Debit == 50m && l.PartyType == null);            // Dr bank (CliQ)
        Assert.Contains(e.Lines, l => l.Credit == 161.475m && l.PartyType == "Driver");  // Cr driver advances: 200 − 38.525 carried
        Assert.Equal(e.TotalDebit, e.TotalCredit);

        // The rest: a new open advance, no money moved, no entry of its own, shown as open and in the next settlement.
        var carried = (await ReadAsync<AdvanceDetails>(await o.Client.GetAsync(U(o, $"/advances/{stl.CarriedAdvance!.Id}")))).Advance;
        Assert.Equal((38.525m, CashCodes.Carried, stl.Number), (carried.Amount, carried.PaymentMethod, carried.CarriedFrom!.Number));
        Assert.Null(carried.JournalEntry);
        Assert.Equal("advance.carried", await (await o.Client.PostAsJsonAsync(U(o, $"/advances/{carried.Id}/void"), new CashVoidRequest("x", carried.RowVersion))).ProblemCodeAsync());
        Assert.Equal("advance.carried", await (await o.Client.PutAsJsonAsync(U(o, $"/advances/{carried.Id}"),
            new AdvanceRequest(w.Driver.Id, Today, 1m, "Cash", w.CashBox, null, null, null, null, carried.RowVersion))).ProblemCodeAsync());
        var account = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Equal(-38.525m, account.Balance);
        Assert.Equal(carried.Id, Assert.Single(account.OpenAdvances).Id);
        var print = await (await o.Client.GetAsync(U(o, $"/settlements/{stl.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("CLIQ-7788", print, StringComparison.Ordinal);
        Assert.Contains(carried.Number, print, StringComparison.Ordinal);

        // Dashboard reminder: the carried balance with "Repaid by driver: amount (method, reference)".
        var dash = await ReadAsync<AccountingDashboardResponse>(await o.Client.GetAsync(U(o, "/accounting/dashboard")));
        var reminder = Assert.Single(dash.Reminders, r => r.Key == AccountingReminderKeys.CarriedAdvances);
        Assert.Equal((1, 38.525m), (reminder.Count, reminder.Amount));
        Assert.Contains("CLIQ-7788", Assert.Single(reminder.Items!), StringComparison.Ordinal);
        Assert.Contains("50.000", reminder.Items![0], StringComparison.Ordinal);

        // Next settlement: a new trip pays off the carried advance; the first settlement cannot be voided before the second.
        var t2 = await w.Trip("R-2", w.Driver, null);
        var next = (await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, null)))).Settlement;
        Assert.Equal(111.475m - 38.525m, next.NetPayable);
        var first = (await ReadAsync<SettlementDetails>(await o.Client.GetAsync(U(o, $"/settlements/{stl.Id}")))).Settlement;
        Assert.Equal("settlement.carriedSettled", await (await o.Client.PostAsJsonAsync(U(o, $"/settlements/{stl.Id}/void"), new CashVoidRequest("x", first.RowVersion))).ProblemCodeAsync());
        await (await o.Client.PostAsJsonAsync(U(o, $"/settlements/{next.Id}/void"), new CashVoidRequest("Redo", next.RowVersion))).EnsureOkAsync();
        await (await o.Client.PostAsJsonAsync(U(o, $"/settlements/{stl.Id}/void"), new CashVoidRequest("Redo", first.RowVersion))).EnsureOkAsync();
        Assert.True((await EntryAsync(stl.JournalEntry!)).Reversed);
        Assert.Equal(CashCodes.Void, (await ReadAsync<AdvanceDetails>(await o.Client.GetAsync(U(o, $"/advances/{carried.Id}")))).Advance.Status);
        var reopened = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        Assert.Equal(adv.Advance.Id, Assert.Single(reopened.OpenAdvances).Id);   // only the original advance, with both trips open
        Assert.Equal(2, reopened.OpenTrips.Count);
        Assert.Equal(2 * 111.475m - 200m, reopened.Balance);
        Assert.DoesNotContain((await ReadAsync<AccountingDashboardResponse>(await o.Client.GetAsync(U(o, "/accounting/dashboard")))).Reminders,
            r => r.Key == AccountingReminderKeys.CarriedAdvances);
        _ = t1;
        _ = t2;
    }

    [Fact]
    public async Task Repayment_by_bank_transfer_when_deductions_exceed_the_wages_keeps_the_rest_on_driver_advances()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        await w.Trip("D-1", w.Driver, null);   // wage 111.475, fine 150 → net −38.525, no advances
        Assert.Equal("settlement.repaymentNotNeeded", await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, 5m, fine: 10m))).ProblemCodeAsync());
        var stl = (await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"), Settle(w, 10m, "Bank", "TRF-9", fine: 150m)))).Settlement;
        Assert.Equal((10m, 28.525m, "Bank"), (stl.RepaidAmount, stl.CarriedForward, stl.PaymentMethod));
        var (e, _) = await EntryAsync(stl.JournalEntry!);
        Assert.Contains(e.Lines, l => l.Debit == 10m && l.PartyType == null);              // Dr bank
        Assert.Contains(e.Lines, l => l.Debit == 28.525m && l.PartyType == "Driver");      // Dr driver advances: the carried rest
        Assert.Contains(e.Lines, l => l.Credit == 150m);                                   // Cr driver deductions
        Assert.Equal(e.TotalDebit, e.TotalCredit);
        Assert.Equal(-28.525m, (await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")))).Balance);
    }

    [Fact]
    public async Task A_partner_who_owes_the_company_repays_by_cash_bank_transfer_or_CliQ()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var trip = await w.Trip("PR-1", w.PartnerDriver, w.Partner.Id);
        var cost = trip.PartnerCost;
        await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), new PartnerPaymentRequest(w.Partner.Id, Today, cost, "CliQ", null, w.Bank, "CLIQ-1", null, null))).EnsureOkAsync();
        PartnerPaymentRequest Repay(decimal amount, string method, string? reference) =>
            new(w.Partner.Id, Today, amount, method, method == "Cash" ? w.CashBox : null, method == "Cash" ? null : w.Bank, reference, null, null, CashCodes.Repayment);
        Assert.Equal("partnerPayment.repaymentExceedsOwed", await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Repay(1m, "Cash", null))).ProblemCodeAsync());

        // The paid trip is voided: no longer refused — the partner now owes the company its cost.
        var current = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{trip.Id}")))).Trip;
        await (await o.Client.PostAsJsonAsync(U(o, $"/trips/{trip.Id}/void"), new TripVoidRequest("Cancelled load", current.RowVersion, CashCodes.Return))).EnsureOkAsync();
        Assert.Equal(-cost, (await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")))).BalanceDue);
        Assert.Equal("partnerPayment.exceedsBalance", await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, Today, 1m, "Cash", w.CashBox, null, null, null, null))).ProblemCodeAsync());
        Assert.Equal("partnerPayment.repaymentExceedsOwed", await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Repay(cost + 0.001m, "Cash", null))).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Repay(1m, "Bank", null))).StatusCode);   // reference required

        // Cash, then bank transfer, then CliQ for the rest.
        var cash = (await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Repay(10m, "Cash", null)))).Payment;
        Assert.Equal(CashCodes.Repayment, cash.Kind);
        var (e, _) = await EntryAsync(cash.JournalEntry!);
        Assert.Contains(e.Lines, l => l.Debit == 10m && l.PartyType == null);        // Dr cash box
        Assert.Contains(e.Lines, l => l.Credit == 10m && l.PartyType == "Partner");  // Cr partner payable
        await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Repay(20m, "Bank", "TRF-2"))).EnsureOkAsync();
        var last = (await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"), Repay(cost - 30m, "CliQ", "CLIQ-3")))).Payment;
        Assert.Equal(0m, (await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")))).BalanceDue);
        var statement = await ReadAsync<StatementResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/statement")));
        Assert.Equal(3, statement.Lines.Count(l => l.Kind == "Repayment" && l.Increase > 0));
        Assert.Equal(0m, statement.Closing);
        var voucher = await (await o.Client.GetAsync(U(o, $"/partner-payments/{last.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("CLIQ-3", voucher, StringComparison.Ordinal);

        // Void of a repayment reverses it: the partner owes that part again.
        await (await o.Client.PostAsJsonAsync(U(o, $"/partner-payments/{cash.Id}/void"), new CashVoidRequest("Counted twice", cash.RowVersion))).EnsureOkAsync();
        Assert.True((await EntryAsync(cash.JournalEntry!)).Reversed);
        Assert.Equal(-10m, (await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")))).BalanceDue);
    }
}
