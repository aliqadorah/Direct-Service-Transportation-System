using DirectService.Platform.Contracts.Security;
using DirectService.Platform.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Platform.Infrastructure.Persistence;

/// <summary>
/// The Platform part of the composed <see cref="MasterDbContext"/> (platform design a.4, d): the core.* entity configurations of this
/// assembly (all core tables except AuditLogs, Shared, and MaintenanceLogs, kept for Accounting), the foreign keys of Shared tables to the
/// anchors (<see cref="CrossModuleRelationships"/>) and the platform reference data: the permission rows of every module (each module owns
/// its keys, <see cref="IPermissionSeedProvider"/>) and the TRIAL plan, with the same <c>HasData</c> values, Ids and PublicIds as before.
/// </summary>
public sealed class PlatformModelContributor(IEnumerable<IPermissionSeedProvider> modules) : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlatformModelContributor).Assembly);
        CrossModuleRelationships.Apply(modelBuilder);
        PlatformReferenceDataSeed.Apply(modelBuilder, modules);
    }
}
