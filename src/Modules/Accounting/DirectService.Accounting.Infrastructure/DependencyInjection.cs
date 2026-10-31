using DirectService.Accounting.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Accounting.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers this module's part of the model (<see cref="AccountingModelContributor"/>). The host calls it before the Transportation
    /// infrastructure: the contributors are applied in registration order, and the transport foreign keys to acc.* tables name the
    /// accounting entity types (Stage 3 session 3).
    /// </summary>
    public static IServiceCollection AddAccountingInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IModelContributor, AccountingModelContributor>();
        return services;
    }
}
