using System.Security.Claims;
using System.Threading.RateLimiting;
using DirectService.Transportation.Application;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Infrastructure;
using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Transportation.Web;
using DirectService.Transportation.Web.Endpoints;
using DirectService.Transportation.Web.Pipeline;
using DirectService.Transportation.Web.Realtime;
using DirectService.Transportation.Web.Security;
using DirectService.Transportation.Web.Workers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------- services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddOptions<AppOptions>().Bind(config.GetSection(AppOptions.Section));

// Key ring of the auth cookies, antiforgery tokens and stored credentials (DT-221): see DataProtectionSetup.
DataProtectionSetup.Add(builder.Services, config, builder.Environment);

builder.Services.AddAppAuthentication();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = AuthCookies.AntiforgeryHeader;
    o.Cookie.Name = AuthCookies.Antiforgery;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddOpenApi("v1", OpenApiSetup.Configure);
builder.Services.AddSignalR();
builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddScoped<AuthFlow>();
// The design-system services also render the static account pages on the server (SCR-01…05).
DirectService.Transportation.Web.Client.Services.ClientServices.AddClientUi(builder.Services);
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
builder.Services.AddHealthChecks().AddDbContextCheck<MasterDbContext>("database", tags: ["ready"]);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authLimit = config.GetValue("RateLimits:AuthPerMinutePerIp", 20);
    var apiLimit = config.GetValue("RateLimits:ApiPerMinutePerUser", 600);
    o.AddPolicy(RateLimits.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1) }));
    var tenantLimit = config.GetValue("RateLimits:ApiPerMinutePerTenant", 3000);
    // Per user (or address) and, chained, per tenant (API-07): one tenant cannot use up the platform's capacity for the others.
    o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(http => RateLimitPartition.GetTokenBucketLimiter(
            http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions { TokenLimit = apiLimit, TokensPerPeriod = apiLimit, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true })),
        PartitionedRateLimiter.Create<HttpContext, string>(http => http.Items[typeof(DirectService.Transportation.Application.Identity.SessionPrincipal)]
                is DirectService.Transportation.Application.Identity.SessionPrincipal { TenantId: long tenant }
            ? RateLimitPartition.GetTokenBucketLimiter("t:" + tenant.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => new TokenBucketRateLimiterOptions { TokenLimit = tenantLimit, TokensPerPeriod = tenantLimit, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true })
            : RateLimitPartition.GetNoLimiter("no-tenant")));
    o.OnRejected = async (ctx, ct) =>
    {
        var lang = ctx.HttpContext.RequestServices.GetService<IRequestContext>()?.Language ?? "ar";
        await ProblemWriter.WriteAsync(ctx.HttpContext, ErrorCodes.RateLimited, StatusCodes.Status429TooManyRequests, lang).ConfigureAwait(false);
    };
});

if (config.GetValue("Jobs:Enabled", true))
{
    builder.Services.AddHostedService<LockExpiryWorker>();
    builder.Services.AddHostedService<PendingPostingWorker>();
    builder.Services.AddHostedService<EInvoiceWorker>();
    builder.Services.AddHostedService<ExportWorker>();
    builder.Services.AddHostedService<TenantLifecycleWorker>();
    builder.Services.AddHostedService<SessionCleanupWorker>();   // DT-211
    builder.Services.AddHostedService<SupportWindowWorker>();   // DT-220
    builder.Services.AddHostedService<EmailOutboxWorker>();   // DT-251
}

var app = builder.Build();

// ---------------------------------------------------------------- pipeline (order = security model, step 2 §9)
// Behind a TLS-terminating proxy or load balancer (container hosting, NFR-22): the scheme and client address come from the proxy's
// X-Forwarded-* headers, so HTTPS redirection, secure cookies and the per-address rate limits work. Off by default; the proxies to
// trust must be listed (Hosting:KnownProxies addresses and/or Hosting:KnownNetworks CIDR ranges): trusting any sender would let a
// client forge its address past the sign-in rate limit and into the audit log, so the host refuses to start without them.
if (config.GetValue("Hosting:BehindProxy", false))
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto };
    var proxies = config.GetSection("Hosting:KnownProxies").Get<string[]>() ?? [];
    var networks = config.GetSection("Hosting:KnownNetworks").Get<string[]>() ?? [];
    if (proxies.Length == 0 && networks.Length == 0)
        throw new InvalidOperationException("Hosting:BehindProxy is on, but neither Hosting:KnownProxies nor Hosting:KnownNetworks is configured.");
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    foreach (var p in proxies) forwarded.KnownProxies.Add(System.Net.IPAddress.Parse(p));
    foreach (var n in networks) forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(n));
    app.UseForwardedHeaders(forwarded);
}
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStatusCodePages(async status =>
{
    var http = status.HttpContext;
    if (http.Response.HasStarted || http.Response.ContentLength > 0) return;
    var code = http.Response.StatusCode switch
    {
        401 => ErrorCodes.Unauthenticated,
        403 => ErrorCodes.Forbidden,
        404 => ErrorCodes.NotFound,
        _ => null,
    };
    if (code is null) return;
    var lang = http.RequestServices.GetService<IRequestContext>()?.Language ?? "ar";
    await ProblemWriter.WriteAsync(http, code, http.Response.StatusCode, lang).ConfigureAwait(false);
});
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter(); // after authentication so the API limit is partitioned per user (SR-02)
app.UseMiddleware<RequestContextMiddleware>();
app.UseMiddleware<CompanyContextMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();
app.UseMiddleware<ExpiredFormMiddleware>();   // DT-221: an account form with an outdated token is shown again, never a raw 400

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

var api = app.MapGroup("/api/v1").AddEndpointFilter<AntiforgeryFilter>();
api.MapAuthEndpoints();
api.MapMeEndpoints();
api.MapTenantEndpoints();
var company = api.MapGroup("/companies/{company}").AddEndpointFilter<WriteGuardFilter>().AddEndpointFilter<ExportAuditFilter>();
company.MapUserAdminEndpoints();
company.MapSettingsEndpoints();
company.MapLockEndpoints();
company.MapAccountingEndpoints();
company.MapBudgetEndpoints();
company.MapMasterDataEndpoints();
company.MapPricingEndpoints();
company.MapTripEndpoints();
company.MapInvoicingEndpoints();
company.MapEInvoicingEndpoints();
company.MapCashEndpoints();
company.MapApArEndpoints();
company.MapLedgerReportEndpoints();
company.MapReportEndpoints();
company.MapCollaborationEndpoints();
api.MapPlatformEndpoints();
company.MapOnboardingEndpoints();
app.MapBillingWebhook();
app.MapHub<LiveHub>(LiveHub.Path);

// Blazor Web App (ADR-02): static assets (fingerprinted, compressed) and the WebAssembly UI.
// Short-circuited: static files skip session validation, the API rate limiter and the request-context queries
// (security headers and HTTPS redirection run before routing and still apply).
app.MapStaticAssets().ShortCircuit();
app.MapRazorComponents<DirectService.Transportation.Web.Components.App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DirectService.Transportation.Web.Client.Routes).Assembly);

// DT-209: never serve with a database that lacks this version's migrations (Development applies them; elsewhere the host refuses).
await DirectService.Transportation.Web.Pipeline.DatabaseVersion.EnsureCurrentAsync(app).ConfigureAwait(false);

// Operations command (DT-131): `create-staff <email> <full name>` creates a platform staff account and e-mails a set-password link, then exits.
if (args is ["create-staff", var staffEmail, .. var staffName])
{
    using var opsScope = app.Services.CreateScope();
    var outcome = await opsScope.ServiceProvider.GetRequiredService<DirectService.Transportation.Application.Platform.StaffAccountService>()
        .CreateAsync(staffEmail, string.Join(' ', staffName), CancellationToken.None).ConfigureAwait(false);
    // The email went into the outbox (DT-251); the command sends it now rather than leave it to the next start of the host.
    using (var sendScope = app.Services.CreateScope())
        await sendScope.ServiceProvider.GetRequiredService<DirectService.Transportation.Infrastructure.Email.EmailDispatcher>().RunOnceAsync(CancellationToken.None).ConfigureAwait(false);
    Console.WriteLine(outcome);
    return;
}

// First-run provider setup (owner feedback 13 Oct 2026, item 8a): while no platform staff account exists, a one-time setup code is
// generated and written to the log (or Platform:SetupCode is used, never logged); /provider/setup then creates the first staff account.
try
{
    using var setupScope = app.Services.CreateScope();
    await setupScope.ServiceProvider.GetRequiredService<DirectService.Transportation.Application.Platform.ProviderSetupService>()
        .AnnounceAsync(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ProviderSetup"), CancellationToken.None).ConfigureAwait(false);
}
catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
{
    // The database is not reachable yet: the setup code is announced at the next start.
#pragma warning disable CA1848
    app.Logger.LogWarning(ex, "Provider first-run setup: the database could not be read; the setup code will be announced at the next start.");
#pragma warning restore CA1848
}

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point (public for WebApplicationFactory in the API tests).</summary>
public partial class Program;
