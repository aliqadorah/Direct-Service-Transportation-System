using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner testing 18 Oct 2026 (DT-209): the trips list failed for every role on a development database that had not received the latest
/// migration. Here trips exist before the latest migration is applied: the database is taken back to the migration before
/// <c>Rev6_ReceiptNoSearch</c> (its Down drops the search column and index) and forward again (its Up adds the persisted column, which
/// SQL Server fills for the existing trips) — then the list, its search and its totals open for every preset role.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TripListMigrationTests(ApiFactory f)
{
    private const string BeforeLatest = "20261018070410_Rev6_PostJournalEntries";

    [Fact]
    public async Task Trips_list_opens_for_every_preset_role_with_trips_created_before_the_latest_migration()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var receipts = new[] { "MIG-0001", "mig 0002", "MIG-0003" };
        foreach (var r in receipts) await w.Trip(r, w.Driver, null);

        // One user of each preset role (the Owner is the signed-up user).
        var roles = (await o.Client.GetFromJsonAsync<List<RoleResponse>>($"/api/v1/companies/{o.CompanyId}/roles"))!;
        var clients = new Dictionary<string, HttpClient> { [PresetRoles.Owner] = o.Client };
        foreach (var role in new[] { PresetRoles.Administrator, PresetRoles.Accountant, PresetRoles.Dispatcher, PresetRoles.Viewer })
            clients[role] = await MemberAsync(o, roles.Single(r => r.IsPreset && r.Name == role).Id);

        // The latest migration, rolled back and applied again with these trips in the database.
        // The shared test database may hold the 500,000 trips of TripPerformanceTests: rebuilding the persisted column and its index over them
        // can take minutes on a busy machine, longer than the 30-second default (DT-225: a timeout left the database half-migrated and every
        // later test failed). Whatever happens here, the database is brought to the latest migration again before the next test.
        await f.DbAsync(async db =>
        {
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(15));
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            try
            {
                await migrator.MigrateAsync(BeforeLatest);
                Assert.Null(await db.Database.SqlQueryRaw<int?>("SELECT COL_LENGTH('trn.TRN_Trips', 'ReceiptNoSearch') AS [Value]").SingleAsync());
            }
            finally
            {
                await migrator.MigrateAsync();
            }
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            return true;
        });
        Assert.Equal(0, await f.DbAsync(db => db.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS [Value] FROM trn.TRN_Trips WHERE ReceiptNoSearch IS NULL OR ReceiptNoSearch <> ReceiptNoKey COLLATE Latin1_General_100_BIN2").SingleAsync()));

        foreach (var (role, client) in clients)
        {
            var list = await client.GetAsync($"/api/v1/companies/{o.CompanyId}/trips?pageSize=50");
            Assert.True(list.StatusCode == HttpStatusCode.OK, $"{role}: {(int)list.StatusCode} {await list.Content.ReadAsStringAsync()}");
            var page = (await list.Content.ReadFromJsonAsync<TripPage>())!;
            Assert.Equal(receipts.Length, page.Page.TotalCount);

            var found = (await client.GetFromJsonAsync<TripPage>($"/api/v1/companies/{o.CompanyId}/trips?q={Uri.EscapeDataString("0002")}"))!;
            Assert.Equal("mig 0002", Assert.Single(found.Page.Items).ReceiptNo);   // any part of the receipt number, case and spaces ignored
        }
    }

    private async Task<HttpClient> MemberAsync(SignedInUser owner, string roleId)
    {
        var userName = "mig." + Guid.NewGuid().ToString("N")[..10];
        await (await owner.Client.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/users",
            new CreateUserRequest("Migration " + userName, userName, null, ApiHelpers.Password, roleId, false))).EnsureOkAsync();
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var signIn = (await (await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName))).Content.ReadFromJsonAsync<SignInResponse>())!;
        if (signIn.Next == "twoFactorOffer") await (await client.PostAsync("/api/v1/auth/2fa/skip", null)).EnsureOkAsync();   // Owner/Administrator offer
        await client.UseAntiforgeryAsync();
        return client;
    }
}
