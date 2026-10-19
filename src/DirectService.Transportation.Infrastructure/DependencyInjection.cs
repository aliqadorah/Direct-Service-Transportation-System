using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Infrastructure.EInvoicing;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Infrastructure.Email;
using DirectService.Transportation.Infrastructure.Files;
using DirectService.Transportation.Infrastructure.Identity;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Transportation.Infrastructure.Persistence.Interceptors;
using DirectService.Transportation.Infrastructure.Printing;
using DirectService.Transportation.Infrastructure.Security;
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

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddScoped<LiveChangeInterceptor>();
        services.AddScoped<IAfterCommit>(sp => sp.GetRequiredService<LiveChangeInterceptor>());
        services.AddDbContext<MasterDbContext>((sp, o) => o
            .UseSqlServer(cs, sql => sql.CommandTimeout(60))
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

        services.AddOptions<UrlTokenOptions>().Bind(configuration.GetSection(UrlTokenOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<UrlTokenOptions>, UrlTokenOptionsValidator>();
        services.AddSingleton<IUrlTokenService, UrlTokenService>();
        services.AddScoped<INumberingService, NumberingService>();
        services.AddScoped<IMonthlyRateLock, MonthlyRateLock>();
        services.AddSingleton<IPdfRenderer, ChromiumPdfRenderer>();
        services.AddSingleton<IQrCodeRenderer, QrCodeRenderer>();
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
        services.AddSingleton<Application.Collaboration.IFileStore, Files.LocalFileStore>();
        services.AddSingleton<Application.Collaboration.IExportQueue, Files.ChannelExportQueue>();
        services.AddSingleton<IEInvoiceSignal>(sp => sp.GetRequiredService<EInvoiceSignal>());
        services.AddScoped<IEntityResolver, EntityResolver>();
        services.AddSingleton<ISpreadsheetService, SpreadsheetService>();
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section));
        services.AddSingleton<IEmailSender, FileEmailSender>();
        return services;
    }
}
