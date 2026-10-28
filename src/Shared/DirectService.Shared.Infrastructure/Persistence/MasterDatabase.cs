using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DirectService.Shared.Infrastructure.Persistence;

/// <summary>
/// How every <see cref="MasterDbContext"/> connects (host, design-time factory, tests): SQL Server, the migrations of
/// <see cref="MigrationsAssembly"/> and the history table <see cref="HistoryTable"/>, set explicitly so nothing depends on EF Core's
/// defaults (platform design d). The history table keeps the default schema (dbo): the idempotent script names it unqualified, as before.
/// </summary>
public static class MasterDatabase
{
    /// <summary>The project holding the 20 migrations and the model snapshot (src/Hosts/DirectService.Database).</summary>
    public const string MigrationsAssembly = "DirectService.Database";

    /// <summary>dbo.__EFMigrationsHistory: the migration ids recorded in every existing database.</summary>
    public const string HistoryTable = "__EFMigrationsHistory";

    public static DbContextOptionsBuilder<MasterDbContext> UseMasterDatabase(this DbContextOptionsBuilder<MasterDbContext> options,
        string connectionString, Action<SqlServerDbContextOptionsBuilder>? sql = null)
    {
        ((DbContextOptionsBuilder)options).UseMasterDatabase(connectionString, sql);
        return options;
    }

    public static DbContextOptionsBuilder UseMasterDatabase(this DbContextOptionsBuilder options, string connectionString,
        Action<SqlServerDbContextOptionsBuilder>? sql = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseSqlServer(connectionString, s =>
        {
            s.MigrationsAssembly(MigrationsAssembly);
            s.MigrationsHistoryTable(HistoryTable);
            sql?.Invoke(s);
        });
    }
}
