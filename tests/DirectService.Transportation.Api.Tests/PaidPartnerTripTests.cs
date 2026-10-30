using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner decision 21 Oct 2026 on DT-228 (DT-230): voiding a partner trip that was already paid needs the partner-payment permission, a
/// reason and a choice — (a) the partner returns it (owes the company until a repayment), (b) deduct it from the next payment(s) (set off on
/// the next payment, shown on its voucher and the statement), (c) the payment was not actually made (void the payment for the trip first).
/// Also the daily limits of administrator-started email changes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PaidPartnerTripTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>A partner trip paid in full by CliQ.</summary>
    private static async Task<(CashTests.World W, TripResponse Trip, PartnerPaymentResponse Payment)> PaidTripAsync(CashTests.World w, string receipt)
    {
        var trip = await w.Trip(receipt, w.PartnerDriver, w.Partner.Id);
        var payment = (await ReadAsync<PartnerPaymentDetails>(await w.Owner.Client.PostAsJsonAsync(U(w.Owner, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, Today, trip.PartnerCost, "CliQ", null, w.Bank, "CLIQ-" + receipt, null, null)))).Payment;
        return (w, trip, payment);
    }

    private static async Task<HttpResponseMessage> VoidAsync(SignedInUser o, TripResponse trip, string? handling, HttpClient? client = null)
    {
        var current = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{trip.Id}")))).Trip;
        return await (client ?? o.Client).PostAsJsonAsync(U(o, $"/trips/{trip.Id}/void"), new TripVoidRequest("Load cancelled by the customer", current.RowVersion, handling));
    }

    private static async Task<PartnerAccountResponse> AccountAsync(CashTests.World w) =>
        await ReadAsync<PartnerAccountResponse>(await w.Owner.Client.GetAsync(U(w.Owner, $"/partners/{w.Partner.Id}/account")));

    private static async Task<StatementResponse> StatementAsync(CashTests.World w) =>
        await ReadAsync<StatementResponse>(await w.Owner.Client.GetAsync(U(w.Owner, $"/partners/{w.Partner.Id}/statement")));

    private Task<List<PartnerOverpayment>> OverpaymentsAsync(string tripToken) => f.DbAsync(async db =>
    {
        var pid = f.Services.GetRequiredService<IUrlTokenService>().Decode<Trip>(tripToken)!.Value;
        return await db.Set<PartnerOverpayment>().IgnoreQueryFilters().Where(o => o.Trip.PublicId == pid).ToListAsync();
    });

    private Task<List<string>> TripHistoryAsync(string tripToken) => f.DbAsync(async db =>
    {
        var pid = f.Services.GetRequiredService<IUrlTokenService>().Decode<Trip>(tripToken)!.Value;
        var id = await db.Set<Trip>().IgnoreQueryFilters().Where(t => t.PublicId == pid).Select(t => t.Id).SingleAsync();
        return await db.Set<AuditLog>().IgnoreQueryFilters().Where(a => a.EntityType == "TRN.Trip" && a.EntityId == id).Select(a => a.Action + " " + a.NewValues).ToListAsync();
    });

    private Task<JournalEntry> EntryAsync(DocRef entry) => f.DbAsync(async db =>
    {
        var pid = f.Services.GetRequiredService<IUrlTokenService>().Decode<JournalEntry>(entry.Id)!.Value;
        return await db.Set<JournalEntry>().IgnoreQueryFilters().Include(x => x.Lines).SingleAsync(x => x.PublicId == pid);
    });

    [Fact]
    public async Task Without_a_choice_the_void_is_answered_with_the_amount_and_the_last_payment_then_a_return_leaves_the_partner_owing()
    {
        var (w, trip, payment) = await PaidTripAsync(await CashTests.WorldAsync(f), "PA-1");
        var o = w.Owner;
        var asked = await VoidAsync(o, trip, null);
        var problem = await asked.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((HttpStatusCode.Conflict, "trip.partnerPaid"), (asked.StatusCode, problem.GetProperty("code").GetString()));
        Assert.Equal(trip.PartnerCost, problem.GetProperty("overpaid").GetDecimal());
        Assert.Equal(payment.Id, problem.GetProperty("paymentId").GetString());
        Assert.Equal(payment.Number, problem.GetProperty("paymentNumber").GetString());
        Assert.Equal("Active", (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{trip.Id}")))).Trip.Status);   // nothing changed

        // (a) The partner will return it.
        await (await VoidAsync(o, trip, CashCodes.Return)).EnsureOkAsync();
        var account = await AccountAsync(w);
        Assert.Equal((-trip.PartnerCost, 0m), (account.BalanceDue, account.ToDeduct));
        var row = Assert.Single(await OverpaymentsAsync(trip.Id));
        Assert.Equal((CashCodes.Return, trip.PartnerCost, "Load cancelled by the customer"), (row.Handling, row.Amount, row.Reason));
        Assert.Contains(await TripHistoryAsync(trip.Id), h => h.StartsWith("Void", StringComparison.Ordinal) && h.Contains(trip.PartnerCost.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var statement = await StatementAsync(w);
        Assert.Contains(statement.Lines, l => l.Kind == "Overpaid" && l.Document.Number == "PA-1" && l.Increase == 0 && l.Decrease == 0);
        Assert.Equal(-trip.PartnerCost, statement.Closing);

        // The repayment closes it: Dr cash, Cr partner payable.
        var repaid = (await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, Today, trip.PartnerCost, "Cash", w.CashBox, null, null, null, null, CashCodes.Repayment)))).Payment;
        var e = await EntryAsync(repaid.JournalEntry!);
        Assert.Contains(e.Lines, l => l.Credit == trip.PartnerCost && l.PartyType == "Partner");
        Assert.Equal(0m, (await AccountAsync(w)).BalanceDue);
    }

    [Fact]
    public async Task Deduct_sets_the_amount_off_against_the_next_payment_shown_on_the_voucher_and_the_statement()
    {
        var (w, trip, _) = await PaidTripAsync(await CashTests.WorldAsync(f), "PB-1");
        var o = w.Owner;
        await (await VoidAsync(o, trip, CashCodes.Deduct)).EnsureOkAsync();
        var account = await AccountAsync(w);
        Assert.Equal((-trip.PartnerCost, trip.PartnerCost), (account.BalanceDue, account.ToDeduct));

        // Two new trips: the balance due is their cost less what was paid for the voided one.
        var t2 = await w.Trip("PB-2", w.PartnerDriver, w.Partner.Id);
        var t3 = await w.Trip("PB-3", w.PartnerDriver, w.Partner.Id);
        var due = t2.PartnerCost + t3.PartnerCost - trip.PartnerCost;
        Assert.Equal(due, (await AccountAsync(w)).BalanceDue);
        Assert.Equal("partnerPayment.exceedsBalance", await (await o.Client.PostAsJsonAsync(U(o, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, Today, due + 0.001m, "Cash", w.CashBox, null, null, null, null))).ProblemCodeAsync());
        var next = (await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, Today, due, "Bank", null, w.Bank, "TRF-PB", null, null)))).Payment;
        Assert.Equal(trip.PartnerCost, next.DeductedAmount);   // set off with this payment
        var e = await EntryAsync(next.JournalEntry!);           // the money paid only: no line for the deduction
        Assert.Equal(2, e.Lines.Count);
        Assert.Contains(e.Lines, l => l.Debit == due && l.PartyType == "Partner");
        account = await AccountAsync(w);
        Assert.Equal((0m, 0m), (account.BalanceDue, account.ToDeduct));
        var voucher = await (await o.Client.GetAsync(U(o, $"/partner-payments/{next.Id}/print?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains((t2.PartnerCost + t3.PartnerCost).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), voucher, StringComparison.Ordinal);   // due before the deduction
        var statement = await StatementAsync(w);
        var line = Assert.Single(statement.Lines, l => l.Document.Number == next.Number);
        Assert.Contains(trip.PartnerCost.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture), line.Description, StringComparison.Ordinal);
        Assert.Contains(statement.Lines, l => l.Kind == "Overpaid" && l.Document.Number == "PB-1");
        Assert.Equal(0m, statement.Closing);

        // Voiding that payment gives the deduction back.
        await (await o.Client.PostAsJsonAsync(U(o, $"/partner-payments/{next.Id}/void"), new CashVoidRequest("Wrong account", next.RowVersion))).EnsureOkAsync();
        Assert.Equal(trip.PartnerCost, (await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, Today, due, "Cash", w.CashBox, null, null, null, null)))).Payment.DeductedAmount);
    }

    [Fact]
    public async Task Not_actually_paid_voids_the_payment_for_the_trip_and_the_trip_then_voids_without_a_choice()
    {
        var (w, trip, payment) = await PaidTripAsync(await CashTests.WorldAsync(f), "PC-1");
        var o = w.Owner;
        Assert.Equal("trip.partnerPaid", await (await VoidAsync(o, trip, null)).ProblemCodeAsync());
        var other = await CashTests.WorldAsync(f);
        var foreign = await other.Trip("PC-X", other.PartnerDriver, other.Partner.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Client.PostAsJsonAsync(U(o, $"/partner-payments/{payment.Id}/void"),
            new CashVoidRequest("Not paid", payment.RowVersion, foreign.Id))).StatusCode);   // another company's trip is not found
        var voided = (await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, $"/partner-payments/{payment.Id}/void"),
            new CashVoidRequest("The bank returned the CliQ transfer", payment.RowVersion, trip.Id)))).Payment;
        Assert.Equal("Void", voided.Status);
        var row = Assert.Single(await OverpaymentsAsync(trip.Id));
        Assert.Equal((CashCodes.NotPaid, payment.Amount), (row.Handling, row.Amount));
        Assert.Contains(await TripHistoryAsync(trip.Id), h => h.StartsWith("PartnerPaymentNotMade", StringComparison.Ordinal) && h.Contains(payment.Number, StringComparison.Ordinal));
        Assert.Equal(trip.PartnerCost, (await AccountAsync(w)).BalanceDue);   // the trip is owed again

        // Now nothing is paid: the trip voids with no choice; the balance returns to zero.
        await (await VoidAsync(o, trip, null)).EnsureOkAsync();
        Assert.Equal(0m, (await AccountAsync(w)).BalanceDue);
        Assert.Contains((await StatementAsync(w)).Lines, l => l.Kind == "Overpaid" && l.Description.Contains(payment.Number, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_user_without_the_partner_payment_permission_cannot_void_a_paid_partner_trip()
    {
        var (w, trip, _) = await PaidTripAsync(await CashTests.WorldAsync(f), "PD-1");
        var o = w.Owner;
        var role = await ReadAsync<RoleResponse>(await o.Client.PostAsJsonAsync(U(o, "/roles"),
            new RoleRequest("Trip clerk " + Guid.NewGuid().ToString("N")[..6], [PermissionKeys.TripsView, PermissionKeys.TripsEdit, PermissionKeys.TripsVoid], null)));
        var userName = "t." + Guid.NewGuid().ToString("N")[..10];
        await (await o.Client.PostAsJsonAsync(U(o, "/users"), new CreateUserRequest("Trip Clerk", userName, null, ApiHelpers.Password, role.Id, false))).EnsureOkAsync();
        var clerk = f.NewClient();
        await clerk.UseAntiforgeryAsync();
        await (await clerk.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName))).EnsureOkAsync();
        await clerk.UseAntiforgeryAsync();
        var refused = await VoidAsync(o, trip, CashCodes.Return, clerk);
        Assert.Equal((HttpStatusCode.Forbidden, "trip.partnerPaidPermission"), (refused.StatusCode, await refused.ProblemCodeAsync()));
        Assert.Empty(await OverpaymentsAsync(trip.Id));

        // An unpaid partner trip: the same user voids it as before.
        var unpaid = await w.Trip("PD-2", w.PartnerDriver, w.Partner.Id);
        await (await VoidAsync(o, unpaid, null, clerk)).EnsureOkAsync();
    }

    [Fact]
    public async Task Administrator_started_email_changes_have_daily_limits_per_administrator_and_per_user()
    {
        var owner = await f.SignUpOwnerAsync();
        var roles = (await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles")))!;
        async Task<string> UserAsync()
        {
            var r = await owner.Client.PostAsJsonAsync(U(owner, "/users"), new CreateUserRequest("Staff", "s." + Guid.NewGuid().ToString("N")[..10], null, ApiHelpers.Password,
                roles.Single(x => x.Name == PresetRoles.Accountant).Id, false));
            return (await ReadAsync<UserListItem>(r)).Id;
        }
        Task<HttpResponseMessage> StartAsync(string user) =>
            owner.Client.PostAsJsonAsync(U(owner, $"/users/{user}/email"), new StartEmailChangeRequest($"m-{Guid.NewGuid():N}@example.test"));

        var first = await UserAsync();
        for (var i = 0; i < 3; i++) await (await StartAsync(first)).EnsureOkAsync();
        var perUser = await StartAsync(first);
        var limit = await perUser.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(((HttpStatusCode)429, "user.emailChangeLimitUser"), (perUser.StatusCode, limit.GetProperty("code").GetString()));
        Assert.Contains("3", limit.GetProperty("detail").GetString(), StringComparison.Ordinal);

        var second = await UserAsync();
        for (var i = 0; i < 2; i++) await (await StartAsync(second)).EnsureOkAsync();   // 5 by this administrator
        var perAdmin = await StartAsync(await UserAsync());
        Assert.Equal(((HttpStatusCode)429, "user.emailChangeLimitAdmin"), (perAdmin.StatusCode, await perAdmin.ProblemCodeAsync()));
    }
}
