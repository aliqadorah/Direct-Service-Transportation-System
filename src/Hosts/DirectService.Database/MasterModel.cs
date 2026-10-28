using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Transportation.Infrastructure.Persistence;

namespace DirectService.Database;

/// <summary>
/// Every module's <see cref="IModelContributor"/>, for contexts created outside the host's services (design-time factory, tests). The host
/// gets the same set from each module's registration; a new system adds its contributor here and in its own registration.
/// </summary>
public static class MasterModel
{
    public static IReadOnlyList<IModelContributor> Contributors { get; } = [new SharedModelContributor(), new TransportationModelContributor()];
}
