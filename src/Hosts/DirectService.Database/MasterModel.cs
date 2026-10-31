using DirectService.Accounting.Infrastructure.Persistence;
using DirectService.Platform.Infrastructure.Persistence;
using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Transportation.Infrastructure.Persistence;

namespace DirectService.Database;

/// <summary>
/// Every module's <see cref="IModelContributor"/>, for contexts created outside the host's services (design-time factory, tests). The host
/// gets the same set from each module's registration; a new system adds its contributor here and in its own registration.
/// Accounting comes first: the Transportation contributor names its entity types in the foreign keys to acc.* tables (Stage 3 session 3).
/// The Platform contributor seeds the permission rows of every module (IPermissionSeedProvider): a new system passes its own here too.
/// </summary>
public static class MasterModel
{
    public static IReadOnlyList<IModelContributor> Contributors { get; } =
        [new AccountingModelContributor(), new SharedModelContributor(), new TransportationModelContributor(), new PlatformModelContributor([new Transportation.Infrastructure.Persistence.Seed.ReferenceDataSeed()])];
}
