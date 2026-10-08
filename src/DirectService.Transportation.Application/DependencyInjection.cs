using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Application;

public static class DependencyInjection
{
    /// <summary>Registers the application layer (use-case services and request-scoped context).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<DataScope>();
        services.AddScoped<IDataScope>(sp => sp.GetRequiredService<DataScope>());
        services.AddScoped<IChangeContext, ChangeContext>();
        services.AddScoped<RequestContext>();
        services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());

        services.AddScoped<AccessService>();
        services.AddScoped<GrantGuard>();
        services.AddScoped<SessionService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        services.AddScoped<MeService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<RoleService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<CompanyProvisioningService>();
        services.AddScoped<TenantService>();
        services.AddScoped<CompanySettingsService>();
        services.AddScoped<RecordLockService>();
        services.AddScoped<IRecordLockGuard>(sp => sp.GetRequiredService<RecordLockService>());
        services.AddScoped<IPeriodGuard, PeriodGuard>();

        // Accounting core and posting engine (B2)
        services.AddScoped<PostingEngine>();
        services.AddScoped<PostingSources>();
        services.AddScoped<PendingPostingService>();
        services.AddScoped<PartyResolver>();
        services.AddScoped<ChartOfAccountsService>();
        services.AddScoped<OpeningBalanceService>();
        services.AddScoped<PostingKeyService>();
        services.AddScoped<CashBankService>();
        services.AddScoped<PeriodService>();
        services.AddScoped<JournalService>();
        services.AddScoped<JournalExportService>();
        services.AddScoped<ChartImportService>();
        services.AddScoped<JournalImportService>();

        // Master data and field configuration (B3)
        services.AddScoped<CustomFieldService>();
        services.AddScoped<StandardFieldService>();
        services.AddScoped<ColumnViewService>();
        services.AddScoped<MasterDataSupport>();
        services.AddScoped<DriverService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<PartnerService>();
        services.AddScoped<MaterialService>();
        services.AddScoped<LoadingSiteService>();
        services.AddScoped<SiteService<Domain.Trn.LoadingSite>>(sp => sp.GetRequiredService<LoadingSiteService>());
        services.AddScoped<UnloadingSiteService>();
        services.AddScoped<SiteService<Domain.Trn.UnloadingSite>>(sp => sp.GetRequiredService<UnloadingSiteService>());
        services.AddScoped<OwnershipTypeService>();
        services.AddScoped<MasterExchangeService>();
        return services;
    }
}
