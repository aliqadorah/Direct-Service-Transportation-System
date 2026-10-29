using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Infrastructure.EInvoicing;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Infrastructure.Email;
using DirectService.Transportation.Infrastructure.Identity;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Shared.Infrastructure;
using DirectService.Shared.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers EF Core (SQL Server, interceptor), ASP.NET Core Identity on core.Users (custom store,
    /// no AspNet* tables, no roles), URL tokens, numbering and the development email sender.
    /// The host must register Data Protection (key ring location is a deployment setting).
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

        services.AddIdentityCore<User>(o =>
            {
                // Revision 6: a user created with a username may have no email. Email and username stay unique platform-wide
                // (UX_Users_NormalizedEmail / UX_Users_NormalizedUserName, checked first by the services; the user name check covers both).
                o.User.RequireUniqueEmail = false;
                o.SignIn.RequireConfirmedEmail = true;
                // Password rules come from the tenant policy (TenantPasswordValidator); keep the built-in ones neutral.
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 1;
                // FR-175 / DT-204: per-account backstop — 20 failed attempts from any source → 15-minute lock (5 per account + IP: SignInThrottle).
                o.Lockout.MaxFailedAccessAttempts = SignInThrottle.PerAccountLimit;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                // DT-212: authenticator codes work once (ReplaySafeAuthenticatorTokenProvider), for sign-in, set-up and the profile alike.
                o.Tokens.AuthenticatorTokenProvider = ReplaySafeAuthenticatorTokenProvider.Name;
            })
            .AddUserStore<UserStore>()
            .AddPasswordValidator<TenantPasswordValidator>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<StaffInvitationTokenProvider>(Application.Platform.StaffService.InvitationTokenProvider)
            .AddTokenProvider<ReplaySafeAuthenticatorTokenProvider>(ReplaySafeAuthenticatorTokenProvider.Name);
        services.AddOptions<StaffInvitationTokenProviderOptions>();   // 48-hour staff invitation links (owner feedback 13 Oct 2026)
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24)); // SEC-04

        services.AddScoped<IMonthlyRateLock, MonthlyRateLock>();
        // E-invoicing (B7): connectors, credentials, locks, worker signal.
        services.AddOptions<EInvoicingOptions>().Bind(configuration.GetSection(EInvoicingOptions.Section));
        services.AddOptions<Application.Platform.PlatformOptions>().Bind(configuration.GetSection(Application.Platform.PlatformOptions.Section));
        services.AddSingleton<Application.Platform.IPaymentGateway, Billing.FakePaymentGateway>();
        services.AddScoped<Application.Platform.IPlatformNumbers, Billing.PlatformNumbers>();
        services.AddScoped<Application.Platform.IPlatformLock, Billing.SqlPlatformLock>();
        services.AddOptions<JoFotaraOptions>().Bind(configuration.GetSection(JoFotaraOptions.Section));
        services.AddHttpClient(JoFotaraConnector.HttpClientName).RedactLoggedHeaders(["Client-Id", "Secret-Key"]);
        services.AddScoped<IEInvoiceConnector, JoFotaraConnector>();
        services.AddScoped<IEInvoiceConnector, SimulatorConnector>();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IEInvoiceStore, EInvoiceStore>();
        services.AddSingleton<EInvoiceSignal>();
        services.AddSingleton<IEInvoiceSignal>(sp => sp.GetRequiredService<EInvoiceSignal>());
        // System email (DT-250…DT-253): every email into the outbox; the worker sends it with the active mode's transport.
        services.AddOptions<Application.Platform.SystemEmailOptions>().Bind(configuration.GetSection(Application.Platform.SystemEmailOptions.Section));
        services.AddScoped<IEmailSender, OutboxEmailSender>();
        services.AddSingleton<Application.Platform.IEmailProtector, DataProtectionEmailProtector>();
        services.AddSingleton<EmailOutboxSignal>();
        services.AddSingleton<Application.Platform.IEmailOutboxSignal>(sp => sp.GetRequiredService<EmailOutboxSignal>());
        services.AddSingleton<EmailDispatchState>();
        services.AddScoped<EmailDispatcher>();
        services.AddSingleton<Application.Platform.IEmailTransport, FileEmailTransport>();
        services.AddSingleton<Application.Platform.IEmailTransport, GraphEmailTransport>();
        services.AddHttpClient(GraphEmailTransport.HttpClientName).RedactLoggedHeaders(["Authorization"]);
        return services;
    }
}
