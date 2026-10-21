using System.Net.Http.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Trn;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// DT-231 (GitHub CI run of 21 Oct 2026): settlements, advances and partner payments made before <c>Rev6_SettlementRepayment</c> upgrade
/// with the right values (no repayment, nothing carried, net unchanged, every partner payment a payment with no deduction) and the same
/// balances; a database holding driver repayments, carried advances or choices for paid partner trips refuses to be rolled back past the
/// migrations that introduced them — with the reason, and with nothing lost. On its own database.
/// </summary>
public sealed class SettlementUpgradeTests(SettlementUpgradeFactory f) : IClassFixture<SettlementUpgradeFactory>
{
    private const string BeforeRepayment = "20261019094417_Rev6_TwoFactorLastStep";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>Migrates to <paramref name="target"/> (null = latest); returns the SQL error message when refused.</summary>
    private Task<string?> MigrateAsync(string? target) => f.DbAsync(async db =>
    {
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        try
        {
            await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
            return null;
        }
        catch (SqlException e)
        {
            return e.Message;
        }
    });

    private Task<int?> ColumnAsync(string table, string column) =>
        f.DbAsync(db => db.Database.SqlQueryRaw<int?>("SELECT COL_LENGTH({0}, {1}) AS [Value]", table, column).SingleAsync());

    private Task<long> IdAsync<T>(string token) where T : AuditableEntity => f.DbAsync(async db =>
    {
        var pid = f.Services.GetRequiredService<IUrlTokenService>().Decode<T>(token)!.Value;
        return await db.Set<T>().IgnoreQueryFilters().Where(x => x.PublicId == pid).Select(x => x.Id).SingleAsync();
    });

    [Fact]
    public async Task Settlements_made_before_the_repayment_migration_upgrade_with_the_right_values_and_repayments_block_a_rollback()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;

        // Before: an ordinary settlement (trips, an advance, a deduction, net paid in cash) and a partner payment.
        await w.Trip("UP-1", w.Driver, null);
        await w.Trip("UP-2", w.Driver, null);
        await (await o.Client.PostAsJsonAsync(U(o, "/advances"), new AdvanceRequest(w.Driver.Id, Today, 40m, "Cash", w.CashBox, null, null, null, null, null))).EnsureOkAsync();
        var stl = (await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"), new SettlementRequest(w.Driver.Id, Today, null, null,
            [new SettlementDeductionRequest("Fine", 5m, "Late")], "Cash", w.CashBox, null, null, null)))).Settlement;
        var partnerTrip = await w.Trip("UP-P1", w.PartnerDriver, w.Partner.Id);
        var pay = (await ReadAsync<PartnerPaymentDetails>(await o.Client.PostAsJsonAsync(U(o, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, Today, 10m, "Bank", null, w.Bank, "TRF-UP", null, null)))).Payment;
        var driverBefore = await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")));
        var partnerBefore = await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")));
        var stlId = await IdAsync<Settlement>(stl.Id);

        // Back to the schema before the repayment migration: the columns are gone, the row keeps the old meaning.
        Assert.Null(await MigrateAsync(BeforeRepayment));
        Assert.Null(await ColumnAsync("trn.TRN_Settlements", "RepaidAmount"));
        Assert.Null(await ColumnAsync("trn.TRN_PartnerPayments", "Kind"));
        Assert.Equal(stl.NetPayable, await f.DbAsync(db => db.Database.SqlQueryRaw<decimal>(
            "SELECT [NetPayable] AS [Value] FROM trn.TRN_Settlements WHERE [Id] = {0}", stlId).SingleAsync()));

        // Upgrade: the new constraint holds for the old rows and every new column has the right value.
        Assert.Null(await MigrateAsync(null));
        Assert.Empty(await f.DbAsync(db => db.Database.GetPendingMigrationsAsync()));
        var row = await f.DbAsync(db => db.Set<Settlement>().IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == stlId));
        Assert.Equal((stl.GrossWages, stl.AdvancesTotal, stl.DeductionsTotal, stl.NetPayable, 0m, 0m),
            (row.GrossWages, row.AdvancesTotal, row.DeductionsTotal, row.NetPayable, row.RepaidAmount, row.CarriedForward));
        Assert.Equal(row.GrossWages - row.AdvancesTotal - row.DeductionsTotal, row.NetPayable);
        var payId = await IdAsync<PartnerPayment>(pay.Id);
        var payRow = await f.DbAsync(db => db.Set<PartnerPayment>().IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == payId));
        Assert.Equal((CashCodes.Payment, 0m, 10m), (payRow.Kind, payRow.DeductedAmount, payRow.Amount));
        Assert.False(await f.DbAsync(db => db.Set<Advance>().IgnoreQueryFilters().AnyAsync(a => a.CarriedFromSettlementId != null)));
        Assert.Equal(driverBefore.Balance, (await ReadAsync<DriverAccountResponse>(await o.Client.GetAsync(U(o, $"/drivers/{w.Driver.Id}/account")))).Balance);
        Assert.Equal(partnerBefore.BalanceDue, (await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")))).BalanceDue);
        Assert.Equal(stl.Number, (await ReadAsync<SettlementDetails>(await o.Client.GetAsync(U(o, $"/settlements/{stl.Id}")))).Settlement.Number);

        // A partial driver repayment (net −40 → repaid 15, 25 carried): the schema before cannot hold it — the rollback is refused with the
        // reason and the data stays as it is.
        await (await o.Client.PostAsJsonAsync(U(o, "/advances"), new AdvanceRequest(w.Driver.Id, Today, 40m, "Cash", w.CashBox, null, null, null, null, null))).EnsureOkAsync();
        var repaid = (await ReadAsync<SettlementDetails>(await o.Client.PostAsJsonAsync(U(o, "/settlements"),
            new SettlementRequest(w.Driver.Id, Today, null, null, [], "Cash", w.CashBox, null, null, null, 15m)))).Settlement;
        Assert.Equal((15m, 25m), (repaid.RepaidAmount, repaid.CarriedForward));
        var refused = await MigrateAsync(BeforeRepayment);
        Assert.Contains("Rev6_SettlementRepayment cannot be rolled back", refused, StringComparison.Ordinal);
        Assert.Null(await MigrateAsync(null));   // the later migrations it had already taken back are applied again
        var kept = (await ReadAsync<SettlementDetails>(await o.Client.GetAsync(U(o, $"/settlements/{repaid.Id}")))).Settlement;
        Assert.Equal((15m, 25m, 0m), (kept.RepaidAmount, kept.CarriedForward, kept.NetPayable));
        Assert.Equal(25m, (await ReadAsync<AdvanceDetails>(await o.Client.GetAsync(U(o, $"/advances/{kept.CarriedAdvance!.Id}")))).Advance.Amount);

        // A paid partner trip voided with "Deduct": the partner-overpayment migration refuses first; nothing is rolled back.
        var current = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{partnerTrip.Id}")))).Trip;
        await (await o.Client.PostAsJsonAsync(U(o, $"/trips/{partnerTrip.Id}/void"), new TripVoidRequest("Cancelled", current.RowVersion, CashCodes.Deduct))).EnsureOkAsync();
        Assert.Contains("Rev6_PartnerOverpayments cannot be rolled back", await MigrateAsync(BeforeRepayment), StringComparison.Ordinal);
        Assert.Empty(await f.DbAsync(db => db.Database.GetPendingMigrationsAsync()));
        Assert.Equal(1, await f.DbAsync(db => db.Set<PartnerOverpayment>().IgnoreQueryFilters().CountAsync()));
        Assert.Equal(10m, (await ReadAsync<PartnerAccountResponse>(await o.Client.GetAsync(U(o, $"/partners/{w.Partner.Id}/account")))).ToDeduct);
    }
}
