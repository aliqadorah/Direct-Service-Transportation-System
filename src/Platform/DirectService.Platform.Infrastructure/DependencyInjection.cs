using DirectService.Platform.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Platform.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The Platform part of the infrastructure (platform design a.6): the core.* model with the permission rows of every module's
    /// <see cref="Contracts.Security.IPermissionSeedProvider"/>. The host calls it next to the modules' registrations.
    /// </summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IModelContributor, PlatformModelContributor>();
        return services;
    }
}
