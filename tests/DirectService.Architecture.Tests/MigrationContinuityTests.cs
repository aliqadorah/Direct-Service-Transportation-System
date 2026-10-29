using DirectService.Database;
using DirectService.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Architecture.Tests;

/// <summary>
/// One migration history across the restructuring (platform design d, i.3 item 5): the 20 migrations of the restore tag, by the same ids,
/// in the Database project; the history table dbo.__EFMigrationsHistory as before; the composed model equal to the snapshot. Nothing here
/// connects to a database.
/// </summary>
public sealed class MigrationContinuityTests
{
    /// <summary>The migration ids at the restore tag <c>pre-modules-final-2026-10-27</c>, as every existing database records them.</summary>
    private static readonly string[] TagMigrationIds =
    [
        "20261008105537_InitialMasterDatabase",
        "20261017111652_Rev6_UserNameSignIn",
        "20261017113752_Rev6_RequireTwoFactor",
        "20261017121155_Rev6_JournalReferenceAndDrafts",
        "20261017122900_Rev6_ReceiptAllocations",
        "20261017131559_Rev6_SupplierBillDeductions",
        "20261017141251_Rev6_ReturnDeductions",
        "20261017145631_Rev6_Budgets",
        "20261017151850_Rev6_JournalTemplates",
        "20261018070410_Rev6_PostJournalEntries",
        "20261018092900_Rev6_ReceiptNoSearch",
        "20261019094417_Rev6_TwoFactorLastStep",
        "20261021071856_Rev6_SettlementRepayment",
        "20261021074616_Rev6_PendingEmail",
        "20261021092930_Rev6_PartnerOverpayments",
        "20261022114807_Rev6_SystemEmail",
        "20261025120358_Rev6_JournalLineTax",
        "20261026070033_Rev6_BudgetProfitLoss",
        "20261027103252_Rev6_PartnerTripCoverage",
        "20261027113420_Rev6_PreviousBalances",
    ];

    [Fact]
    public void The_migrations_are_the_tags_by_id_in_the_database_project()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var migrations = db.GetService<IMigrationsAssembly>();
        Assert.Equal(MasterDatabase.MigrationsAssembly, migrations.Assembly.GetName().Name);
        Assert.Equal(typeof(DesignTimeDbContextFactory).Assembly, migrations.Assembly);
        // Stages 2–4 add no migration (design d); Stage 5 adds Rev7_ProductsAndSubscriptions after these.
        Assert.Equal(TagMigrationIds, migrations.Migrations.Keys);
        Assert.NotNull(migrations.ModelSnapshot);
        var elsewhere = Golden.AppAssemblies.SelectMany(a => a.GetTypes())
            .Where(t => typeof(Migration).IsAssignableFrom(t) || typeof(ModelSnapshot).IsAssignableFrom(t)).Select(t => t.FullName);
        Assert.Empty(elsewhere);
    }

    [Fact]
    public void The_history_table_is_the_unqualified_EFMigrationsHistory_as_before()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        var sql = RelationalOptionsExtension.Extract(db.GetService<IDbContextOptions>());
        Assert.Equal("__EFMigrationsHistory", sql.MigrationsHistoryTableName);
        // No schema: the script keeps "[__EFMigrationsHistory]" (byte-identical idempotent script); the table is dbo's (DT-293).
        Assert.Null(sql.MigrationsHistoryTableSchema);
        Assert.Equal(MasterDatabase.MigrationsAssembly, sql.MigrationsAssembly);
        Assert.Contains("[__EFMigrationsHistory]", db.GetService<IHistoryRepository>().GetCreateScript(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_composed_model_has_no_change_against_the_snapshot()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges(), "The model differs from MasterDbContextModelSnapshot: a migration would be generated.");
    }

    [Fact]
    public void The_host_registers_the_same_model_contributors_as_the_database_project()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MasterDb"] = DesignTimeDbContextFactory.LocalDevelopment,
        }).Build();
        DirectService.Transportation.Infrastructure.DependencyInjection.AddInfrastructure(services, config);
        DirectService.Platform.Infrastructure.DependencyInjection.AddPlatformInfrastructure(services, config);
        var registered = services.Where(d => d.ServiceType == typeof(IModelContributor)).Select(d => d.ImplementationType).ToList();
        Assert.Equal(MasterModel.Contributors.Select(c => c.GetType()), registered);
    }
}
