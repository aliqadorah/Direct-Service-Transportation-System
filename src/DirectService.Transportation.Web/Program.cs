using System.Security.Claims;
using System.Threading.RateLimiting;
using DirectService.Transportation.Application;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Infrastructure;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Transportation.Web;
using DirectService.Transportation.Web.Endpoints;
using DirectService.Transportation.Web.Pipeline;
using DirectService.Transportation.Web.Realtime;
using DirectService.Transportation.Web.Security;
using DirectService.Transportation.Web.Workers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------------------------------------------------------------- services
builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddOptions<AppOptions>().Bind(config.GetSection(AppOptions.Section));

var dp = builder.Services.AddDataProtection().SetApplicationName("DirectService.Transportation")
    .PersistKeysToFileSystem(new DirectoryInfo(config["DataProtection:KeysPath"] ?? ".local/keys"));
if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi();

builder.Services.AddAppAuthentication();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "__Host-dst-af";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddOpenApi("v1", OpenApiSetup.Configure);
builder.Services.AddSignalR();
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
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => RateLimitPartition.GetTokenBucketLimiter(
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new TokenBucketRateLimiterOptions { TokenLimit = apiLimit, TokensPerPeriod = apiLimit, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true }));
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
}

var app = builder.Build();

// ---------------------------------------------------------------- pipeline (order = security model, step 2 §9)
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
var company = api.MapGroup("/companies/{company}").AddEndpointFilter<WriteGuardFilter>();
company.MapUserAdminEndpoints();
company.MapSettingsEndpoints();
company.MapLockEndpoints();
company.MapAccountingEndpoints();
company.MapMasterDataEndpoints();
company.MapPricingEndpoints();
app.MapHub<LiveHub>(LiveHub.Path);

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point (public for WebApplicationFactory in the API tests).</summary>
public partial class Program;
