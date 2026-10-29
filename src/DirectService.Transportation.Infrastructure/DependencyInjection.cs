using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Infrastructure.EInvoicing;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Shared.Infrastructure;
using DirectService.Shared.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers EF Core (SQL Server, interceptors) with the shared building blocks (URL tokens, numbering …), this module's part of the
    /// model and its permission rows, the monthly-rate lock and e-invoicing. Identity, system e-mail and billing are registered by
    /// Platform.Infrastructure (AddPlatformInfrastructure). The host must register Data Protection (key ring location is a deployment setting).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("MasterDb")
            ?? throw new InvalidOperationException("ConnectionStrings:MasterDb is not configured.");

        services.AddSharedInfrastructure(configuration);
        services.AddSingleton<IModelContributor, TransportationModelContributor>();
        services.AddSingleton<DirectService.Platform.Contracts.Security.IPermissionSeedProvider, Persistence.Seed.ReferenceDataSeed>();
        services.AddDbContext<MasterDbContext>((sp, o) => o
            .UseMasterDatabase(cs, sql => sql.CommandTimeout(60))
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>(), sp.GetRequiredService<LiveChangeInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<MasterDbContext>());

        services.AddScoped<IMonthlyRateLock, MonthlyRateLock>();
        // E-invoicing (B7): connectors, credentials, locks, worker signal.
        services.AddOptions<EInvoicingOptions>().Bind(configuration.GetSection(EInvoicingOptions.Section));
        services.AddOptions<JoFotaraOptions>().Bind(configuration.GetSection(JoFotaraOptions.Section));
        services.AddHttpClient(JoFotaraConnector.HttpClientName).RedactLoggedHeaders(["Client-Id", "Secret-Key"]);
        services.AddScoped<IEInvoiceConnector, JoFotaraConnector>();
        services.AddScoped<IEInvoiceConnector, SimulatorConnector>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IEInvoiceStore, EInvoiceStore>();
        services.AddSingleton<EInvoiceSignal>();
        services.AddSingleton<IEInvoiceSignal>(sp => sp.GetRequiredService<EInvoiceSignal>());
        return services;
    }
}
