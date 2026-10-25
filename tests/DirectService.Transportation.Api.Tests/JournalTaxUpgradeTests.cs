using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Accounting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// DT-259 (owner request 25 Oct 2026): entries posted before <c>Rev6_JournalLineTax</c> keep working after the upgrade — their lines take the
/// entry's Reference, carry no tax code, and can still be read, reported and voided; every company gets the default tax codes; no account is
/// added or marked. Once a line carries a tax code the migration refuses to be rolled back, with the reason, and nothing is lost. Own database.
/// </summary>
public sealed class JournalTaxUpgradeTests(JournalTaxUpgradeFactory f) : IClassFixture<JournalTaxUpgradeFactory>
{
    private const string BeforeTax = "20261022114807_Rev6_SystemEmail";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

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

    private Task<List<string?>> LineReferencesAsync(string number) => f.DbAsync(db => db.Database.SqlQueryRaw<string?>(
        "SELECT l.[Reference] AS [Value] FROM acc.EntryLines l INNER JOIN acc.JournalEntries e ON e.[Id] = l.[JournalEntryId] WHERE e.[Number] = {0} ORDER BY l.[LineNo]", number)
        .ToListAsync());

    [Fact]
    public async Task Entries_made_before_the_tax_migration_upgrade_with_the_entry_reference_on_their_lines_and_tax_in_use_blocks_a_rollback()
    {
        var o = await f.SignUpOwnerAsync();
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var a = (await o.Client.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(x => x.Code);
        var old = await ReadAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            new ManualEntryRequest(Today, "Old rent", [new(a["5202"].Id, 70, 0, null, null, null), new(a["110101"].Id, 0, 70, null, null, null)], null, "OLD-REF")));
        var plain = await ReadAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            new ManualEntryRequest(Today, "No reference", [new(a["5203"].Id, 5, 0, null, null, null), new(a["110101"].Id, 0, 5, null, null, null)], null)));

        // Back to the schema before the migration: no line reference, no tax columns, no tax codes, no tax-account flag.
        Assert.Null(await MigrateAsync(BeforeTax));
        Assert.Null(await ColumnAsync("acc.EntryLines", "Reference"));
        Assert.Null(await ColumnAsync("acc.EntryLines", "TaxCodeId"));
        Assert.Null(await ColumnAsync("acc.Accounts", "IsTaxAccount"));
        Assert.Null(await f.DbAsync(db => db.Database.SqlQueryRaw<int?>("SELECT OBJECT_ID(N'acc.TaxCodes') AS [Value]").SingleAsync()));

        // Upgrade: the lines take the entry's Reference (none stays none), no tax on old lines, the default codes for the company, no tax account.
        Assert.Null(await MigrateAsync(null));
        Assert.Empty(await f.DbAsync(db => db.Database.GetPendingMigrationsAsync()));
        Assert.Equal(["OLD-REF", "OLD-REF"], await LineReferencesAsync(old.Number));
        Assert.Equal(new string?[] { null, null }, await LineReferencesAsync(plain.Number));
        Assert.Equal(0, await f.DbAsync(db => db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM acc.EntryLines WHERE [TaxCodeId] IS NOT NULL").SingleAsync()));
        var codes = await o.Client.GetFromJsonAsync<List<TaxCodeRef>>(U(o, "/accounting/tax-codes/options"));
        Assert.Equal([0m, 1m, 2m, 4m, 5m, 7m, 8m, 10m, 16m], codes!.Select(c => c.Rate));
        Assert.DoesNotContain((await o.Client.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!, x => x.IsTaxAccount);

        // The old entry reads, reports and voids as before.
        var read = await ReadAsync<JournalEntryResponse>(await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{old.Id}")));
        Assert.Equal(["OLD-REF", "OLD-REF"], read.Lines.Select(l => l.Reference));
        Assert.All(read.Lines, l => Assert.Null(l.TaxCode));
        var statement = await o.Client.GetFromJsonAsync<Contracts.Ledger.LedgerReport>(U(o, $"/accounting/reports/account-statement?account={a["5202"].Id}&reference=OLD-REF"));
        Assert.Equal(70m, Assert.Single(statement!.Lines.Items).Debit);
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/journal-entries/{plain.Id}/void"), new VoidRequest("test", plain.RowVersion))).EnsureOkAsync();

        // Tax in use: the rollback is refused with the reason, nothing is rolled back.
        await (await o.Client.PostAsJsonAsync(U(o, "/accounting/accounts/default-tax-accounts"), new { })).EnsureOkAsync();
        a = (await o.Client.GetFromJsonAsync<List<AccountResponse>>(U(o, "/accounting/accounts")))!.ToDictionary(x => x.Code);
        var vat = codes!.Single(c => c.Code == "VAT_JO_16");
        var taxed = await ReadAsync<JournalEntryResponse>(await o.Client.PostAsJsonAsync(U(o, "/accounting/journal-entries"),
            new ManualEntryRequest(Today, "Taxed", [new(a["5299"].Id, 100, 0, null, null, null, null, vat.Id, a["1105"].Id), new(a["110101"].Id, 0, 116, null, null, null)], null)));
        Assert.Equal(3, taxed.Lines.Count);
        var refused = await MigrateAsync(BeforeTax);
        Assert.Contains("Rev6_JournalLineTax cannot be rolled back", refused, StringComparison.Ordinal);
        Assert.Empty(await f.DbAsync(db => db.Database.GetPendingMigrationsAsync()));
        Assert.Equal(16m, (await ReadAsync<JournalEntryResponse>(await o.Client.GetAsync(U(o, $"/accounting/journal-entries/{taxed.Id}")))).Lines[0].TaxAmount);
    }
}
