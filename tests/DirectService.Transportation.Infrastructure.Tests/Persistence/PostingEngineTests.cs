using System.Data.Common;
using System.Security.Cryptography;
using DirectService.Accounting.Application.Posting;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Accounting.Domain.Acc;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Shared.Infrastructure.Persistence.Interceptors;
using DirectService.Shared.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Infrastructure.Tests.Persistence;

/// <summary>
/// Posting engine against SQL Server (PRD 8.6, ADR-09): balanced entries with gap-free numbers, refusals (BR-033,
/// BR-034, FR-094, FR-106), linked reversal (FR-099) and the savepoint → pending posting → retry path (FR-101).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PostingEngineTests(DatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateOnly Day = new(2026, 9, 15);

    private sealed record Ledger(TestTenant Tenant, long Cash, long Revenue, long DriverPayable, long Expense);

    /// <summary>A company with the journal sequence, four accounts and every posting key mapped.</summary>
    private async Task<Ledger> LedgerAsync(bool mapKeys = true)
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            ctx.Add(new NumberSequence { DocumentType = DocumentTypes.JournalEntry, Prefix = "JV-", Digits = 5, NextNumber = 1 });
            var cash = new Account { Code = "110101", NameAr = "الصندوق", AccountType = "Asset" };
            var revenue = new Account { Code = "4101", NameAr = "إيرادات النقل", AccountType = "Revenue" };
            var payable = new Account { Code = "2102", NameAr = "ذمم السائقين", AccountType = "Liability", IsControlAccount = true, ControlPartyType = "Driver" };
            var expense = new Account { Code = "5101", NameAr = "أجور السائقين", AccountType = "Expense" };
            ctx.AddRange(cash, revenue, payable, expense);
            await ctx.SaveChangesAsync(Ct);
            if (mapKeys)
            {
                foreach (var key in PostingKeys.All)
                {
                    var account = key.Key switch
                    {
                        PostingKeys.DriverPayable => payable,
                        PostingKeys.TransportRevenue => revenue,
                        PostingKeys.TradeReceivables => cash,
                        _ => expense,
                    };
                    ctx.Add(new PostingKeyMapping { PostingKey = key.Key, AccountId = account.Id });
                }
                await ctx.SaveChangesAsync(Ct);
            }
            return new Ledger(t, cash.Id, revenue.Id, payable.Id, expense.Id);
        }
    }

    private static PostingEngine Engine(MasterDbContext ctx, DataScope scope, TestTenant t) =>
        new(ctx, new NumberingService(ctx, scope), new PeriodGuard(ctx), new PostingKeyCatalog(), Request(t), new ChangeContext(), TimeProvider.System);

    private static RequestContext Request(TestTenant t) =>
        new() { TenantId = t.TenantId, CompanyId = t.CompanyId, UserId = t.UserId, Language = "en" };

    private static PostingRequest WageEntry(decimal amount, long driverId = 1) => new(Day, "Trip TRP wage", "TRN", "Trip", 1001, false,
    [
        PostingLine.DebitKey(PostingKeys.DriverWageExpense, amount),
        PostingLine.CreditKey(PostingKeys.DriverPayable, amount, new PostingParty("TRN", "Driver", driverId)),
    ]);

    [Fact]
    public async Task A_balanced_entry_posts_with_the_next_journal_number_and_its_lines()
    {
        var l = await LedgerAsync();
        var (ctx, scope) = db.ContextFor(l.Tenant);
        await using (ctx)
        {
            var engine = Engine(ctx, scope, l.Tenant);
            await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
            var entry = await engine.PostAsync(WageEntry(111.475m), Ct); // EX-1 driver wage
            await tx.CommitAsync(Ct);

            Assert.Equal("JV-00001", entry.Number);
            Assert.Equal("2026-09", entry.PeriodMonth);
            Assert.Equal(111.475m, entry.TotalDebit);
            Assert.Equal(entry.TotalDebit, entry.TotalCredit);
            var lines = await ctx.Set<EntryLine>().AsNoTracking().Where(x => x.JournalEntryId == entry.Id).OrderBy(x => x.LineNo).ToListAsync(Ct);
            Assert.Equal([l.Expense, l.DriverPayable], lines.Select(x => x.AccountId));
            Assert.Equal("Driver", lines[1].PartyType);
            Assert.Equal(111.475m, lines[0].Debit);
            Assert.Equal(111.475m, lines[1].Credit);
        }
    }

    [Fact]
    public async Task Unbalanced_entries_are_refused_and_take_no_number()
    {
        var l = await LedgerAsync();
        var (ctx, scope) = db.ContextFor(l.Tenant);
        await using (ctx)
        {
            var engine = Engine(ctx, scope, l.Tenant);
            await using (var tx = await ctx.Database.BeginTransactionAsync(Ct))
            {
                var bad = new PostingRequest(Day, "Manual", "ACC", "Manual", null, true,
                    [new PostingLine(null, l.Cash, 10, 0), new PostingLine(null, l.Revenue, 0, 9.999m)]);
                var ex = await Assert.ThrowsAsync<AppException>(() => engine.PostAsync(bad, Ct));
                Assert.Equal(ErrorCodes.JournalUnbalanced, ex.Code);
                var both = new PostingRequest(Day, "Manual", "ACC", "Manual", null, true,
                    [new PostingLine(null, l.Cash, 10, 10), new PostingLine(null, l.Revenue, 0, 0)]);
                Assert.Equal(ErrorCodes.JournalLineAmount, (await Assert.ThrowsAsync<AppException>(() => engine.PostAsync(both, Ct))).Code);
                var tooPrecise = new PostingRequest(Day, "Manual", "ACC", "Manual", null, true,
                    [new PostingLine(null, l.Cash, 1.0005m, 0), new PostingLine(null, l.Revenue, 0, 1.0005m)]);
                Assert.Equal(ErrorCodes.JournalLineAmount, (await Assert.ThrowsAsync<AppException>(() => engine.PostAsync(tooPrecise, Ct))).Code);
                await tx.CommitAsync(Ct);
            }
            await using (var tx = await ctx.Database.BeginTransactionAsync(Ct))
            {
                Assert.Equal("JV-00001", (await engine.PostAsync(WageEntry(5), Ct)).Number);
                await tx.CommitAsync(Ct);
            }
        }
    }

    [Fact]
    public async Task Entries_dated_in_a_closed_period_are_refused()
    {
        var l = await LedgerAsync();
        var (ctx, scope) = db.ContextFor(l.Tenant);
        await using (ctx)
        {
            ctx.Add(new Period { PeriodMonth = "2026-09", Status = "Closed", ClosedBy = l.Tenant.UserId, ClosedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync(Ct);
            var engine = Engine(ctx, scope, l.Tenant);
            await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
            var ex = await Assert.ThrowsAsync<AppException>(() => engine.PostAsync(WageEntry(5), Ct));
            Assert.Equal(ErrorCodes.PeriodClosed, ex.Code);
            var october = WageEntry(5) with { EntryDate = new DateOnly(2026, 10, 1) };
            Assert.Equal("JV-00001", (await engine.PostAsync(october, Ct)).Number);
        }
    }

    [Fact]
    public async Task Automatic_posting_is_disabled_while_a_required_key_is_unmapped()
    {
        var l = await LedgerAsync(mapKeys: false);
        var (ctx, scope) = db.ContextFor(l.Tenant);
        await using (ctx)
        {
            var engine = Engine(ctx, scope, l.Tenant);
            Assert.Equal(13, (await engine.MissingKeysAsync(Ct)).Count);
            await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
            var ex = await Assert.ThrowsAsync<AppException>(() => engine.PostAsync(WageEntry(5), Ct));
            Assert.Equal(ErrorCodes.PostingKeysMissing, ex.Code);
            // Manual entries name their accounts and are not blocked (DT-21)
            var manual = new PostingRequest(Day, "Manual", "ACC", "Manual", null, true,
                [new PostingLine(null, l.Cash, 10, 0), new PostingLine(null, l.Revenue, 0, 10)]);
            Assert.Equal("JV-00001", (await engine.PostAsync(manual, Ct)).Number);
        }
    }

    [Fact]
    public async Task Control_account_lines_need_a_party_of_the_accounts_type()
    {
        var l = await LedgerAsync();
        var (ctx, scope) = db.ContextFor(l.Tenant);
        await using (ctx)
        {
            var engine = Engine(ctx, scope, l.Tenant);
            await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
            var noParty = new PostingRequest(Day, "Manual", "ACC", "Manual", null, true,
                [new PostingLine(null, l.Expense, 10, 0), new PostingLine(null, l.DriverPayable, 0, 10)]);
            Assert.Equal(ErrorCodes.ControlPartyRequired, (await Assert.ThrowsAsync<AppException>(() => engine.PostAsync(noParty, Ct))).Code);
            var wrongParty = noParty with
            {
                Lines = [noParty.Lines[0], noParty.Lines[1] with { Party = new PostingParty("TRN", "Customer", 5) }],
            };
            Assert.Equal(ErrorCodes.ControlPartyMismatch, (await Assert.ThrowsAsync<AppException>(() => engine.PostAsync(wrongParty, Ct))).Code);
        }
    }

    [Fact]
    public async Task A_reversal_mirrors_the_lines_and_links_both_entries()
    {
        var l = await LedgerAsync();
        var (ctx, scope) = db.ContextFor(l.Tenant);
        await using (ctx)
        {
            var engine = Engine(ctx, scope, l.Tenant);
            await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
            var original = await engine.PostAsync(WageEntry(111.475m), Ct);
            var reversal = await engine.ReverseAsync(original, "Trip voided", null, Ct);

            Assert.Equal("JV-00002", reversal.Number);
            Assert.Equal(original.Id, reversal.ReversesEntryId);
            Assert.Equal(reversal.Id, original.ReversedByEntryId);
            Assert.Equal("Reversed", original.Status);
            Assert.Equal(original.EntryDate, reversal.EntryDate);
            Assert.Equal(("TRN", "Trip", (long?)1001), (reversal.SourceModule, reversal.SourceType, reversal.SourceId));
            var net = await ctx.Set<EntryLine>().AsNoTracking().Where(x => x.JournalEntryId == original.Id || x.JournalEntryId == reversal.Id)
                .GroupBy(x => x.AccountId).Select(g => g.Sum(x => x.Debit - x.Credit)).ToListAsync(Ct);
            Assert.All(net, n => Assert.Equal(0m, n));
            await Assert.ThrowsAsync<AppException>(() => engine.ReverseAsync(original, "again", null, Ct));
        }
    }

    [Fact]
    public async Task A_technical_failure_keeps_the_document_and_queues_a_pending_posting_that_a_retry_posts()
    {
        var l = await LedgerAsync();
        var failing = new FailJournalInserts();
        var scope = new DataScope();
        scope.Set(l.Tenant.TenantId, l.Tenant.CompanyId, l.Tenant.UserId);
        var options = new DbContextOptionsBuilder<MasterDbContext>().UseMasterDatabase(db.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(scope, new ChangeContext(), TimeProvider.System), failing).Options;
        await using var ctx = new MasterDbContext(options, scope, DirectService.Database.MasterModel.Contributors);
        var engine = Engine(ctx, scope, l.Tenant);

        await using (var tx = await ctx.Database.BeginTransactionAsync(Ct))
        {
            var document = new CostCentre { Code = "DOC-1", Name = "stands in for the source document" };
            ctx.Add(document);
            await ctx.SaveChangesAsync(Ct);
            failing.Enabled = true;
            var entry = await engine.PostOrPendAsync(WageEntry(50), Ct);
            failing.Enabled = false;
            await tx.CommitAsync(Ct);
            Assert.Null(entry);
        }

        Assert.True(await ctx.Set<CostCentre>().AnyAsync(c => c.Code == "DOC-1", Ct));
        Assert.False(await ctx.Set<JournalEntry>().AnyAsync(Ct));
        var pending = await ctx.Set<PendingPosting>().AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("TRN", "Trip", 1001L, 1), (pending.SourceModule, pending.SourceType, pending.SourceId, pending.Attempts));
        Assert.Null(pending.ResolvedAt);
        Assert.Contains("Technical error while posting", pending.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("simulated", pending.Reason, StringComparison.Ordinal); // no exception text shown to users (SR-B2-04)
        Assert.Equal(1, await ctx.Set<NumberSequence>().Where(s => s.DocumentType == DocumentTypes.JournalEntry).Select(s => s.NextNumber).SingleAsync(Ct));

        // Retry: no handler registered → stays pending with the reason; with the document's handler → posted and linked.
        var tokens = new UrlTokenService(Options.Create(new UrlTokenOptions { Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));
        var none = new PendingPostingService(ctx, engine, new PostingSources([]), tokens, Request(l.Tenant), TimeProvider.System);
        Assert.False(await none.RetryAsync(pending.Id, Ct));
        var handler = new TripHandler(WageEntry(50));
        var service = new PendingPostingService(ctx, engine, new PostingSources([handler]), tokens, Request(l.Tenant), TimeProvider.System);
        Assert.True(await service.RetryAsync(pending.Id, Ct));
        var resolved = await ctx.Set<PendingPosting>().AsNoTracking().SingleAsync(Ct);
        Assert.NotNull(resolved.ResolvedAt);
        Assert.Equal(3, resolved.Attempts);
        Assert.Equal("JV-00001", handler.Attached?.Number);
    }

    /// <summary>Simulates a technical database failure on the journal insert.</summary>
    private sealed class FailJournalInserts : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("[acc].[JournalEntries]", StringComparison.Ordinal)
                && command.CommandText.Contains("INSERT", StringComparison.Ordinal))
                throw new InvalidOperationException("simulated database failure");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TripHandler(PostingRequest request) : IPostingSourceHandler
    {
        public JournalEntry? Attached { get; private set; }
        public string SourceModule => "TRN";
        public string SourceType => "Trip";
        public Task<PostingRequest?> BuildAsync(long sourceId, CancellationToken ct) => Task.FromResult<PostingRequest?>(request);

        public Task AttachAsync(long sourceId, JournalEntry entry, CancellationToken ct)
        {
            Attached = entry;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<long, Contracts.Accounting.SourceDocumentRef>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<long, Contracts.Accounting.SourceDocumentRef>>(new Dictionary<long, Contracts.Accounting.SourceDocumentRef>());
    }
}
