using DirectService.Platform.Application.Billing;
using DirectService.Platform.Application.Companies;
using DirectService.Platform.Application.Email;
using DirectService.Platform.Application.Errors;
using DirectService.Platform.Application.Identity;
using DirectService.Platform.Application.Staff;
using DirectService.Platform.Application.Tenants;
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
        services.AddSingleton<IErrorText, ErrorText>();
        services.AddScoped<RecordSupport>();

        // Identity, plans and subscription invoices (Stage 2 session 13; registered by AddApplication before, same lifetimes).
        services.AddScoped<AccessService>();
        services.AddScoped<GrantGuard>();
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
        return services;
    }
}
