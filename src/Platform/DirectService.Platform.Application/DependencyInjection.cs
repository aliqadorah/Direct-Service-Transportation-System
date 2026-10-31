using DirectService.Platform.Application.Audit;
using DirectService.Platform.Application.Billing;
using DirectService.Platform.Application.Companies;
using DirectService.Platform.Application.Email;
using DirectService.Platform.Application.Errors;
using DirectService.Platform.Application.Identity;
using DirectService.Platform.Application.Notifications;
using DirectService.Platform.Application.Staff;
using DirectService.Platform.Application.Tenants;
using DirectService.Platform.Contracts.Collaboration;
using DirectService.Platform.Contracts.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Platform.Application;

public static class DependencyInjection
{
    /// <summary>
    /// The Platform services (platform design a.5). The host calls it next to the modules' registrations; a module adds its messages through
    /// <see cref="IErrorTextProvider"/>.
    /// </summary>
    public static IServiceCollection AddPlatformApplication(this IServiceCollection services)
    {
        services.AddScoped<IUserNames, UserNames>();
        services.AddScoped<ICompanyInfo, CompanyInfo>();
        services.AddScoped<CompanySettingsService>();   // profile, logo, market, numbering (Stage 2 session 14; registered by AddApplication before)
        services.AddSingleton<IErrorText, ErrorText>();
        services.AddScoped<RecordSupport>();
        services.AddScoped<RecordHistory>();   // a record’s readable history for every module (Stage 3 session 6c)

        // Identity, plans and subscription invoices (Stage 2 session 13; registered by AddApplication before, same lifetimes).
        services.AddScoped<AccessService>();
        services.AddScoped<GrantGuard>();
        services.AddScoped<PersonalDataService>();   // users' personal data (Stage 2 session 14; registered by AddApplication before)
        services.AddScoped<SessionService>();
        services.AddScoped<AuthService>();
        services.AddScoped<TwoFactorSettingsService>();   // DT-210: 2FA management from the profile
        services.AddSingleton<SignInThrottle>();   // DT-204: failed sign-ins per account + IP
        services.AddScoped<AccountService>();
        services.AddScoped<MeService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<EmailChangeService>();
        services.AddScoped<RoleService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<PlanLimits>();
        services.AddScoped<IPlanLimitCheck>(sp => sp.GetRequiredService<PlanLimits>());   // the module's trip limit (Stage 2 session 14)
        services.AddScoped<BillingService>();

        // Staff, provider set-up, system e-mail, support access and the tenant lifecycle (Stage 2 session 13; registered by AddApplication before, same lifetimes).
        services.AddSingleton<ProviderSetupState>();
        services.AddScoped<ProviderSetupService>();
        services.AddScoped<StaffService>();
        services.AddScoped<SupportAccessService>();
        services.AddScoped<TenantLifecycleService>();
        services.AddScoped<StaffAccountService>();
        services.AddScoped<SystemEmailService>();   // DT-250

        // Tenants, subscriptions, the provider console, onboarding and the company export (Stage 2 session 13; registered by AddApplication before, same lifetimes).
        services.AddScoped<TenantService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<ConsoleService>();
        services.AddScoped<SetupChecklistService>();
        services.AddScoped<DemoCompanyService>();
        services.AddScoped<CompanyExportService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<IExportJobProvider, PlatformExportJobs>();   // the "company" export kind (Stage 2 session 14)

        // In-app notifications (Stage 2 session 14; registered by the module before, same lifetimes); a module adds its types, words and
        // links through INotificationTypeProvider.
        services.AddScoped<NotificationService>();
        services.AddScoped<IUserNotifier>(sp => sp.GetRequiredService<NotificationService>());
        services.AddScoped<NotificationLinks>();
        services.AddScoped<IExportNotifier, ExportNotifications>();
        return services;
    }
}
