using Microsoft.EntityFrameworkCore;

namespace DirectService.Accounting.Infrastructure.Persistence;

/// <summary>
/// The Accounting part of the composed <see cref="MasterDbContext"/> (platform design a.4, d): the configurations of the 21 acc.* tables,
/// cfg.EInvoiceSettings and core.MaintenanceLogs (K-11), moved here from Transportation.Infrastructure in Stage 3 session 3 with the same
/// table, column, constraint and index names. It is applied before the Transportation contributor, whose foreign keys to acc.* tables name
/// these entity types (Transportation's CrossModuleRelationships).
/// </summary>
public sealed class AccountingModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountingModelContributor).Assembly);
    }
}
