using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Transportation.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Tests.Persistence;

/// <summary>
/// The migrations of the Database project land in the same history table as before the restructuring (platform design d, DT-293):
/// dbo.__EFMigrationsHistory, every migration recorded by its id, nothing pending.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MigrationHistoryTests(DatabaseFixture db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_history_table_is_dbo_EFMigrationsHistory_with_every_migration_applied()
    {
        await using var ctx = db.CreateContext(new DataScope());
        var schemas = await ctx.Database.SqlQuery<string>(
            $"SELECT SCHEMA_NAME(schema_id) AS [Value] FROM sys.tables WHERE name = {MasterDatabase.HistoryTable}").ToListAsync(Ct);
        Assert.Equal(["dbo"], schemas);
        Assert.Equal(ctx.Database.GetMigrations(), await ctx.Database.GetAppliedMigrationsAsync(Ct));
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync(Ct));
    }
}
