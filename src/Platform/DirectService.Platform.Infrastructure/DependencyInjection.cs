using DirectService.Platform.Infrastructure.Email;
using DirectService.Platform.Infrastructure.Identity;
using DirectService.Platform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DirectService.Platform.Application.Notifications;
using DirectService.Platform.Application.Email;
using DirectService.Platform.Application.Billing;
using DirectService.Platform.Application.Identity;

namespace DirectService.Platform.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The Platform part of the infrastructure (platform design a.6): the core.* model with the permission rows of every module's
    /// <see cref="Contracts.Security.IPermissionSeedProvider"/>; ASP.NET Core Identity on core.Users (custom store, no AspNet* tables, no
    /// roles) with its token providers; the development payment gateway; system e-mail (outbox, dispatcher, development folder and
    /// Microsoft 365 transports). The host calls it next to the modules' registrations and registers Data Protection.
    /// </summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton<IModelContributor, PlatformModelContributor>();

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
            .AddTokenProvider<StaffInvitationTokenProvider>(StaffInvitations.TokenProvider)
            .AddTokenProvider<ReplaySafeAuthenticatorTokenProvider>(ReplaySafeAuthenticatorTokenProvider.Name);
        services.AddOptions<StaffInvitationTokenProviderOptions>();   // 48-hour staff invitation links (owner feedback 13 Oct 2026)
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24)); // SEC-04

        services.AddOptions<PlatformOptions>().Bind(configuration.GetSection(PlatformOptions.Section));
        services.AddSingleton<IPaymentGateway, Billing.FakePaymentGateway>();
        services.AddScoped<IPlatformNumbers, Billing.PlatformNumbers>();
        services.AddScoped<IPlatformLock, Billing.SqlPlatformLock>();
        // System email (DT-250…DT-253): every email into the outbox; the worker sends it with the active mode's transport.
        services.AddOptions<SystemEmailOptions>().Bind(configuration.GetSection(SystemEmailOptions.Section));
        services.AddScoped<IEmailSender, OutboxEmailSender>();
        services.AddSingleton<IEmailProtector, DataProtectionEmailProtector>();
        services.AddSingleton<EmailOutboxSignal>();
        services.AddSingleton<IEmailOutboxSignal>(sp => sp.GetRequiredService<EmailOutboxSignal>());
        services.AddSingleton<EmailDispatchState>();
        services.AddScoped<EmailDispatcher>();
        services.AddSingleton<IEmailTransport, FileEmailTransport>();
        services.AddSingleton<IEmailTransport, GraphEmailTransport>();
        services.AddHttpClient(GraphEmailTransport.HttpClientName).RedactLoggedHeaders(["Authorization"]);
        return services;
    }
}
