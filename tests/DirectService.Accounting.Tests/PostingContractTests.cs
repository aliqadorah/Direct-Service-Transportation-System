using System.Collections.Concurrent;
using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using DirectService.Accounting.Contracts.Posting;
using Microsoft.AspNetCore.Hosting;
using DirectService.Shared.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Accounting.Tests;

/// <summary>
/// The contract suite's host (own database): a test document type with its <see cref="IPostingSourceHandler"/> registered through the
/// contract, and an interceptor that makes the insert of an entry fail technically while a test switches it on.
/// </summary>
public sealed class PostingContractFactory : ApiFactory
{
    protected override string? DatabaseName => "DirectServiceMaster_PostingContractTests";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s =>
        {
            // Today's registration of a contract handler (with its bridge to the pending postings); moves with the engine in session 4.
            DirectService.Transportation.Application.Accounting.PostingContractRegistration.AddPostingSourceHandler<ContractTestDocuments>(s);
            s.ConfigureDbContext<MasterDbContext>(o => o.AddInterceptors(FailingEntryInsert.Instance));
        });
    }
}

[CollectionDefinition(Name)]
public sealed class PostingContractCollection : ICollectionFixture<PostingContractFactory>
{
    public const string Name = "PostingContract";
}

/// <summary>A test document type: its entries, the entry id stored on it ("attached"), its number.</summary>
public sealed class ContractTestDocuments : IPostingSourceHandler
{
    public const string SourceType = "ContractTest";

    public static ConcurrentDictionary<long, PostingDocument> Documents { get; } = new();

    public static ConcurrentDictionary<long, long> Attached { get; } = new();

    public string Module => "TRN";

    public string Type => SourceType;

    public Task<PostingDocument?> RebuildAsync(long sourceId, CancellationToken ct) => Task.FromResult(Documents.GetValueOrDefault(sourceId));

    public Task AttachAsync(long sourceId, long journalEntryId, CancellationToken ct)
    {
        Attached[sourceId] = journalEntryId;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<long, PostingSourceInfo>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<long, PostingSourceInfo>>(sourceIds.ToDictionary(id => id, id => new PostingSourceInfo($"CT-{id}", null)));
}

/// <summary>A technical failure (not a business refusal) of the insert of an entry, for the flow of the test that switches it on.</summary>
public sealed class FailingEntryInsert : DbCommandInterceptor
{
    public static FailingEntryInsert Instance { get; } = new();

    private static readonly AsyncLocal<bool> Active = new();

    public static IDisposable On()
    {
        Active.Value = true;
        return new Off();
    }

    private sealed class Off : IDisposable
    {
        public void Dispose() => Active.Value = false;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Active.Value && command.CommandText.Contains("[acc].[JournalEntries]", StringComparison.Ordinal) && command.CommandText.Contains("INSERT", StringComparison.Ordinal))
            throw new InvalidOperationException("simulated database failure");
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>
/// The posting contract of design c (<see cref="IAccountingPosting"/>, <see cref="IPostingSourceHandler"/>, <see cref="IPostingKeyProvider"/>)
/// against today's posting engine (Stage 3 session 2, design j "Proven by"): post in the caller's transaction and refuse without one,
/// idempotent post per document, reverse (and void) by document, business failures refuse and never pend, a technical failure pends and the
/// registered handler rebuilds the entry on retry, readiness per product, the posting keys of each product. The same suite runs unchanged
/// when sessions 4–5 move the engine behind the contract.
/// </summary>
[Collection(PostingContractCollection.Name)]
public sealed class PostingContractTests(PostingContractFactory f)
{
    private static long _nextSource = 900_000;

    private sealed record Company(SignedInUser Owner, long TenantId, long CompanyId, long UserId, Dictionary<string, long> Accounts, long Vat16);

    /// <summary>A new tenant; with the transport chart template its posting keys are mapped (D-10).</summary>
    private async Task<Company> CompanyAsync(bool template = true)
    {
        var owner = await f.SignUpOwnerAsync();
        if (template) await (await owner.Client.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/accounting/accounts/template", new { })).EnsureOkAsync();
        await using var c = new SqlConnection(f.Database);
        await c.OpenAsync();
        await using var find = new SqlCommand("SELECT TOP 1 r.TenantId, r.CompanyId, u.Id FROM core.UserCompanyRoles r JOIN core.Users u ON u.Id = r.UserId WHERE u.Email = @e", c);
        find.Parameters.AddWithValue("@e", owner.Email);
        long tenant, company, user;
        await using (var r = await find.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            (tenant, company, user) = (r.GetInt64(0), r.GetInt64(1), r.GetInt64(2));
        }
        var accounts = new Dictionary<string, long>(StringComparer.Ordinal);
        await using (var cmd = new SqlCommand("SELECT Code, Id FROM acc.Accounts WHERE CompanyId = @c", c))
        {
            cmd.Parameters.AddWithValue("@c", company);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) accounts[r.GetString(0)] = r.GetInt64(1);
        }
        await using var tax = new SqlCommand("SELECT TOP 1 Id FROM acc.TaxCodes WHERE Code = 'VAT_JO_16' AND (CompanyId IS NULL OR CompanyId = @c) ORDER BY CompanyId DESC", c);
        tax.Parameters.AddWithValue("@c", company);
        return new Company(owner, tenant, company, user, accounts, (long)(await tax.ExecuteScalarAsync())!);
    }

    /// <summary>Runs code as the company's owner in a scope of the host (as a worker does), optionally inside one transaction.</summary>
    private async Task<T> AsOwnerAsync<T>(Company c, Func<IAccountingPosting, IAppDbContext, Task<T>> work, bool transaction = true)
    {
        await using var scope = f.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<DataScope>().Set(c.TenantId, c.CompanyId, c.UserId);
        var request = sp.GetRequiredService<RequestContext>();
        (request.TenantId, request.CompanyId, request.UserId, request.Language) = (c.TenantId, c.CompanyId, c.UserId, "en");
        var posting = sp.GetRequiredService<IAccountingPosting>();
        var db = sp.GetRequiredService<IAppDbContext>();
        return transaction ? await db.InTransactionAsync(_ => work(posting, db), CancellationToken.None) : await work(posting, db);
    }

    private static DocumentRef NewSource() => new("TRN", ContractTestDocuments.SourceType, Interlocked.Increment(ref _nextSource));

    /// <summary>Wage expense (by posting key) 100 + VAT 16 % to 1105, against cash 116 (explicit account) with a note and a line reference.</summary>
    private static PostingDocument Document(Company c, DocumentRef source, decimal amount = 100m) => new(source, new DateOnly(2026, 10, 1), $"Contract test {source.Id}", "REF-1",
    [
        new PostingLineSpec("TRN.DriverWageExpense", null, amount, 0, TaxCodeId: c.Vat16, TaxAccountId: c.Accounts["1105"]),
        new PostingLineSpec(null, c.Accounts["110101"], 0, amount * 1.16m, Note: "Paid in cash", Reference: "PAY-9"),
    ]);

    private sealed record EntryRow(long Id, string Number, string Status, long? ReversesEntryId, long? ReversedByEntryId, string? VoidReason, decimal TotalDebit);

    private async Task<List<EntryRow>> EntriesAsync(Company c, DocumentRef source)
    {
        await using var con = new SqlConnection(f.Database);
        await con.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT Id, Number, Status, ReversesEntryId, ReversedByEntryId, VoidReason, TotalDebit FROM acc.JournalEntries
            WHERE CompanyId = @c AND SourceModule = @m AND SourceType = @t AND SourceId = @s ORDER BY Id
            """, con);
        cmd.Parameters.AddWithValue("@c", c.CompanyId);
        cmd.Parameters.AddWithValue("@m", source.Module);
        cmd.Parameters.AddWithValue("@t", source.Type);
        cmd.Parameters.AddWithValue("@s", source.Id);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<EntryRow>();
        while (await r.ReadAsync())
            rows.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetInt64(3), r.IsDBNull(4) ? null : r.GetInt64(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetDecimal(6)));
        return rows;
    }

    private async Task<List<(int Attempts, bool Resolved)>> PendingAsync(Company c, DocumentRef source)
    {
        await using var con = new SqlConnection(f.Database);
        await con.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Attempts, CASE WHEN ResolvedAt IS NULL THEN 0 ELSE 1 END FROM acc.PendingPostings WHERE CompanyId = @c AND SourceType = @t AND SourceId = @s ORDER BY Id", con);
        cmd.Parameters.AddWithValue("@c", c.CompanyId);
        cmd.Parameters.AddWithValue("@t", source.Type);
        cmd.Parameters.AddWithValue("@s", source.Id);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<(int, bool)>();
        while (await r.ReadAsync()) rows.Add((r.GetInt32(0), r.GetInt32(1) == 1));
        return rows;
    }

    [Fact]
    public async Task Without_an_ambient_transaction_every_posting_call_is_refused()
    {
        var c = await CompanyAsync();
        var source = NewSource();
        await AsOwnerAsync(c, async (posting, _) =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => posting.PostAsync(Document(c, source), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => posting.PostOrPendAsync(Document(c, source), CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => posting.ReverseAsync(source, "Reversal", null, CancellationToken.None));
            return 0;
        }, transaction: false);
        Assert.Empty(await EntriesAsync(c, source));
        Assert.Empty(await PendingAsync(c, source));
    }

    [Fact]
    public async Task A_post_is_idempotent_per_document_and_after_a_reversal_the_document_posts_again()
    {
        var c = await CompanyAsync();
        var source = NewSource();
        var first = await AsOwnerAsync(c, (posting, _) => posting.PostAsync(Document(c, source), CancellationToken.None));
        Assert.StartsWith("JV-", first.Number, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(first.Token));
        // Again (and through PostOrPend): the same entry, nothing new posted.
        Assert.Equal(first, await AsOwnerAsync(c, (posting, _) => posting.PostAsync(Document(c, source, 999m), CancellationToken.None)));
        Assert.Equal(first, await AsOwnerAsync(c, (posting, _) => posting.PostOrPendAsync(Document(c, source), CancellationToken.None)));
        var entries = await EntriesAsync(c, source);
        Assert.Equal(first.JournalEntryId, Assert.Single(entries).Id);
        Assert.Equal(116m, entries[0].TotalDebit);

        // An edit is reverse + post: the mirror links both ways, the original is Reversed, the document then gets a new entry.
        var mirror = await AsOwnerAsync(c, (posting, _) => posting.ReverseAsync(source, "Edit of contract test", null, CancellationToken.None));
        Assert.NotNull(mirror);
        var second = await AsOwnerAsync(c, (posting, _) => posting.PostAsync(Document(c, source, 200m), CancellationToken.None));
        Assert.NotEqual(first.Number, second.Number);
        entries = await EntriesAsync(c, source);
        Assert.Equal(3, entries.Count);
        Assert.Equal(("Reversed", mirror!.JournalEntryId), (entries[0].Status, entries[0].ReversedByEntryId!.Value));
        Assert.Equal(("Posted", first.JournalEntryId), (entries[1].Status, entries[1].ReversesEntryId!.Value));
        Assert.Equal(("Posted", second.JournalEntryId, 232m), (entries[2].Status, entries[2].Id, entries[2].TotalDebit));
        // The current entry is the new one: reversing again reverses it, not the first.
        Assert.NotNull(await AsOwnerAsync(c, (posting, _) => posting.ReverseAsync(source, "Void of contract test", null, CancellationToken.None)));
        Assert.Equal("Reversed", (await EntriesAsync(c, source))[2].Status);
    }

    [Fact]
    public async Task Reverse_with_a_reason_voids_and_a_document_without_an_entry_has_nothing_to_reverse()
    {
        var c = await CompanyAsync();
        var source = NewSource();
        Assert.Null(await AsOwnerAsync(c, (posting, _) => posting.ReverseAsync(source, "Nothing", null, CancellationToken.None)));
        await AsOwnerAsync(c, (posting, _) => posting.PostAsync(Document(c, source), CancellationToken.None));
        var mirror = await AsOwnerAsync(c, (posting, _) => posting.ReverseAsync(source, "Void of contract test", "Entered twice", CancellationToken.None));
        var entries = await EntriesAsync(c, source);
        Assert.Equal(2, entries.Count);
        Assert.Equal(("Void", "Entered twice", mirror!.JournalEntryId), (entries[0].Status, entries[0].VoidReason, entries[0].ReversedByEntryId!.Value));
        Assert.Null(await AsOwnerAsync(c, (posting, _) => posting.ReverseAsync(source, "Again", null, CancellationToken.None)));
    }

    [Fact]
    public async Task A_business_failure_refuses_the_post_and_never_pends()
    {
        var c = await CompanyAsync();
        var source = NewSource();
        PostingDocument Unbalanced() => new(source, new DateOnly(2026, 10, 1), "Unbalanced", null,
            [new PostingLineSpec(null, c.Accounts["5299"], 100m, 0), new PostingLineSpec(null, c.Accounts["110101"], 0, 90m)]);
        var post = await Assert.ThrowsAsync<AppException>(() => AsOwnerAsync(c, (posting, _) => posting.PostAsync(Unbalanced(), CancellationToken.None)));
        Assert.Equal("journal.unbalanced", post.Code);
        var pend = await Assert.ThrowsAsync<AppException>(() => AsOwnerAsync(c, (posting, _) => posting.PostOrPendAsync(Unbalanced(), CancellationToken.None)));
        Assert.Equal("journal.unbalanced", pend.Code);
        Assert.Empty(await EntriesAsync(c, source));
        Assert.Empty(await PendingAsync(c, source));
    }

    [Fact]
    public async Task A_technical_failure_pends_once_per_document_and_the_registered_handler_rebuilds_the_entry_on_retry()
    {
        var c = await CompanyAsync();
        var source = NewSource();
        ContractTestDocuments.Documents[source.Id] = Document(c, source);
        using (FailingEntryInsert.On())
        {
            Assert.Null(await AsOwnerAsync(c, (posting, _) => posting.PostOrPendAsync(Document(c, source), CancellationToken.None)));
            Assert.Null(await AsOwnerAsync(c, (posting, _) => posting.PostOrPendAsync(Document(c, source), CancellationToken.None)));
            // A plain post refuses instead of pending.
            await Assert.ThrowsAnyAsync<Exception>(() => AsOwnerAsync(c, (posting, _) => posting.PostAsync(Document(c, source), CancellationToken.None)));
        }
        Assert.Empty(await EntriesAsync(c, source));
        Assert.Equal([(2, false)], await PendingAsync(c, source));   // one open row per document (UX_PendingPostings_Source)

        // The pending postings list describes the source through the handler; its retry rebuilds the entry with the handler and attaches it.
        var o = c.Owner;
        var list = await o.Client.GetFromJsonAsync<JsonElement>($"/api/v1/companies/{o.CompanyId}/ledger/pending-postings");
        var row = list.EnumerateArray().Single(p => p.GetProperty("sourceType").GetString() == ContractTestDocuments.SourceType);
        Assert.Equal($"CT-{source.Id}", row.GetProperty("source").GetProperty("number").GetString());
        var retry = await o.Client.PostAsJsonAsync($"/api/v1/companies/{o.CompanyId}/ledger/pending-postings/{row.GetProperty("id").GetString()}/retry", new { });
        await retry.EnsureOkAsync();
        Assert.Equal(1, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("posted").GetInt32());
        var entry = Assert.Single(await EntriesAsync(c, source));
        Assert.Equal(("Posted", 116m), (entry.Status, entry.TotalDebit));
        Assert.Equal(entry.Id, ContractTestDocuments.Attached[source.Id]);
        Assert.Equal([(3, true)], await PendingAsync(c, source));
        // Posting the document now finds its entry.
        Assert.Equal(entry.Id, (await AsOwnerAsync(c, (posting, _) => posting.PostOrPendAsync(Document(c, source), CancellationToken.None)))!.JournalEntryId);
    }

    [Fact]
    public async Task The_lines_reach_the_entry_as_specified_with_the_mapped_account_and_the_tax_line()
    {
        var c = await CompanyAsync();
        var source = NewSource();
        var posted = await AsOwnerAsync(c, (posting, _) => posting.PostAsync(Document(c, source), CancellationToken.None));
        await using var con = new SqlConnection(f.Database);
        await con.OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT l.[LineNo], a.Code, l.Debit, l.Credit, l.Note, l.Reference, l.TaxAmount, t.[LineNo] FROM acc.EntryLines l
            JOIN acc.Accounts a ON a.Id = l.AccountId LEFT JOIN acc.EntryLines t ON t.Id = l.TaxForLineId
            WHERE l.JournalEntryId = @e ORDER BY l.[LineNo]
            """, con);
        cmd.Parameters.AddWithValue("@e", posted.JournalEntryId);
        await using var r = await cmd.ExecuteReaderAsync();
        var lines = new List<(int No, string Account, decimal Debit, decimal Credit, string? Note, string? Reference, decimal? Tax, int? TaxFor)>();
        while (await r.ReadAsync())
            lines.Add((r.GetInt32(0), r.GetString(1), r.GetDecimal(2), r.GetDecimal(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetDecimal(6), r.IsDBNull(7) ? null : r.GetInt32(7)));
        // TRN.DriverWageExpense → 5101 (template mapping); the entry's reference on lines without their own; the tax line last, linked to line 1.
        Assert.Equal(3, lines.Count);
        Assert.Equal((1, "5101", 100m, 0m, (string?)null, "REF-1", (decimal?)16m, (int?)null), lines[0]);
        Assert.Equal((2, "110101", 0m, 116m, "Paid in cash", "PAY-9", (decimal?)null, (int?)null), lines[1]);
        Assert.Equal((3, "1105", 16m, 0m, "REF-1", (int?)1), (lines[2].No, lines[2].Account, lines[2].Debit, lines[2].Credit, lines[2].Reference, lines[2].TaxFor));
        Assert.False(string.IsNullOrEmpty(lines[2].Note));
    }

    [Fact]
    public async Task Readiness_is_checked_per_product_and_the_currency_has_three_decimals()
    {
        var mapped = await CompanyAsync();
        await AsOwnerAsync(mapped, async (posting, _) =>
        {
            await posting.EnsureReadyAsync("TRN", CancellationToken.None);
            await posting.EnsureReadyAsync("ACC", CancellationToken.None);
            Assert.Equal(3, await posting.CurrencyDecimalsAsync(CancellationToken.None));
            return 0;
        }, transaction: false);
        var bare = await CompanyAsync(template: false);
        var e = await Assert.ThrowsAsync<AppException>(() => AsOwnerAsync(bare, async (posting, _) =>
        {
            await posting.EnsureReadyAsync("TRN", CancellationToken.None);
            return 0;
        }, transaction: false));
        Assert.Equal("posting.keysMissing", e.Code);
    }

    [Fact]
    public void Each_product_owns_its_posting_keys_with_unchanged_stored_strings()
    {
        var providers = f.Services.GetServices<IPostingKeyProvider>().ToList();
        Assert.Equal(["ACC", "TRN"], providers.Select(p => p.ProductCode).Order(StringComparer.Ordinal));
        foreach (var p in providers)
            Assert.All(p.Keys, k => Assert.StartsWith(p.ProductCode + ".", k.Key, StringComparison.Ordinal));   // design i.3 item 4
        // The stored strings of design c / E-12 — never changed.
        Assert.Equal(
            ["ACC.OpeningBalanceDifference", "ACC.SalesTaxInput", "ACC.SalesTaxOutput", "ACC.SupplierDeductions",
             "TRN.DiscountAllowed", "TRN.DriverAdvances", "TRN.DriverDeductions", "TRN.DriverPayable", "TRN.DriverWageExpense", "TRN.OtherRevenue",
             "TRN.PartnerCostExpense", "TRN.PartnerPayable", "TRN.TradeReceivables", "TRN.TransportRevenue"],
            providers.SelectMany(p => p.Keys).Select(k => k.Key).Order(StringComparer.Ordinal));
        var keys = providers.SelectMany(p => p.Keys).ToDictionary(k => k.Key);
        Assert.Equal(("5101", false, false), (keys["TRN.DriverWageExpense"].TemplateAccountCode, keys["TRN.DriverWageExpense"].Optional, keys["TRN.DriverWageExpense"].RequiredOnlyDuringSetup));
        Assert.True(keys["ACC.SupplierDeductions"].Optional);
        Assert.True(keys["ACC.OpeningBalanceDifference"].RequiredOnlyDuringSetup);
        Assert.All(keys.Values, k => Assert.False(string.IsNullOrWhiteSpace(k.NameAr) || string.IsNullOrWhiteSpace(k.NameEn)));
    }
}
