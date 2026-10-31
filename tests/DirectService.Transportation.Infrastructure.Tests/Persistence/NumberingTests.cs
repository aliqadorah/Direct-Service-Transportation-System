using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Platform.Domain;
using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Tests.Persistence;

/// <summary>Gap-free numbering under concurrency (BR-038, TST-10) with UPDLOCK, ROWLOCK inside the document transaction.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class NumberingTests(DatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<TestTenant> TenantWithSequenceAsync(string type)
    {
        var t = await db.CreateTenantAsync();
        var (ctx, _) = db.ContextFor(t);
        await using (ctx)
        {
            ctx.Add(new NumberSequence { DocumentType = type, Prefix = "TRP-", Digits = 5, NextNumber = 1 });
            await ctx.SaveChangesAsync(Ct);
        }
        return t;
    }

    [Fact]
    public async Task Concurrent_issuers_get_consecutive_unique_numbers()
    {
        var t = await TenantWithSequenceAsync("TRN.Invoice");
        var numbers = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            var (ctx, scope) = db.ContextFor(t);
            await using (ctx)
            {
                var svc = new NumberingService(ctx, scope);
                await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
                var n = await svc.NextAsync("TRN.Invoice", Ct);
                await Task.Delay(Random.Shared.Next(5, 25), Ct); // hold the lock a little, as a real document save would
                await tx.CommitAsync(Ct);
                return n;
            }
        }));
        Assert.Equal(Enumerable.Range(1, 20).Select(i => $"TRP-{i:00000}").Order(), numbers.Order());
    }

    [Fact]
    public async Task A_rolled_back_document_returns_its_number()
    {
        var t = await TenantWithSequenceAsync("TRN.Receipt");
        var (ctx, scope) = db.ContextFor(t);
        await using (ctx)
        {
            var svc = new NumberingService(ctx, scope);
            await using (var tx = await ctx.Database.BeginTransactionAsync(Ct))
            {
                Assert.Equal("TRP-00001", await svc.NextAsync("TRN.Receipt", Ct));
                await tx.RollbackAsync(Ct);
            }
            await using (var tx = await ctx.Database.BeginTransactionAsync(Ct))
            {
                Assert.Equal("TRP-00001", await svc.NextAsync("TRN.Receipt", Ct));
                await tx.CommitAsync(Ct);
            }
            await using (var tx = await ctx.Database.BeginTransactionAsync(Ct))
            {
                Assert.Equal("TRP-00002", await svc.NextAsync("TRN.Receipt", Ct));
                await tx.CommitAsync(Ct);
            }
        }
    }

    [Fact]
    public async Task Numbers_outside_a_transaction_are_refused()
    {
        var t = await TenantWithSequenceAsync("TRN.Advance");
        var (ctx, scope) = db.ContextFor(t);
        await using (ctx)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new NumberingService(ctx, scope).NextAsync("TRN.Advance", Ct));
        }
    }

    [Fact]
    public async Task Sequences_of_other_companies_are_independent()
    {
        var a = await TenantWithSequenceAsync("TRN.Settlement");
        var b = await TenantWithSequenceAsync("TRN.Settlement");
        foreach (var t in new[] { a, b })
        {
            var (ctx, scope) = db.ContextFor(t);
            await using (ctx)
            {
                await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
                Assert.Equal("TRP-00001", await new NumberingService(ctx, scope).NextAsync("TRN.Settlement", Ct));
                await tx.CommitAsync(Ct);
            }
        }
    }

    [Fact]
    public async Task Seeded_permission_keys_equal_the_code_constants()
    {
        var (ctx, _) = db.ContextFor(await db.CreateTenantAsync());
        await using (ctx)
        {
            var keys = await ctx.Set<Permission>().AsNoTracking().Select(p => p.PermissionKey).ToListAsync(Ct);
            Assert.Equal(PermissionKeys.All.Order(), keys.Order());
        }
    }
}
