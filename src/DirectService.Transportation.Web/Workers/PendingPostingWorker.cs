using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Web.Workers;

/// <summary>
/// Retries pending postings automatically (FR-101, ADR-10) every 5 minutes with back-off (2^attempts minutes, at most
/// hourly). Each company is processed in its own scope as a system job (no acting user); suspended tenants and
/// archived companies are skipped because they are read-only.
/// </summary>
internal sealed partial class PendingPostingWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<PendingPostingWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly string[] ScopeFilters = [QueryFilters.Tenant, QueryFilters.Company];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(log, ex);
            }
        }
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        List<(long TenantId, long CompanyId, long Id)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
            var open = await db.Set<PendingPosting>().IgnoreQueryFilters(ScopeFilters).AsNoTracking()
                .Where(p => p.ResolvedAt == null)
                .Join(db.Set<Company>().IgnoreQueryFilters(ScopeFilters), p => p.CompanyId, c => c.Id, (p, c) => new { p, c })
                .Join(db.Set<Tenant>().IgnoreQueryFilters(ScopeFilters), x => x.c.TenantId, t => t.Id, (x, t) => new { x.p, x.c, t })
                .Where(x => x.c.Status == "Active" && (x.t.Status == "Active" || x.t.Status == "Trial"))
                .Select(x => new { x.p.TenantId, x.p.CompanyId, x.p.Id, x.p.Attempts, x.p.LastAttemptAt })
                .OrderBy(x => x.LastAttemptAt)
                .Take(1000).ToListAsync(ct).ConfigureAwait(false);
            due = open.Where(p => p.LastAttemptAt is null || p.LastAttemptAt < now - Backoff(p.Attempts))
                .Select(p => (p.TenantId, p.CompanyId, p.Id)).ToList();
        }

        var posted = 0;
        foreach (var company in due.GroupBy(p => (p.TenantId, p.CompanyId)))
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<DataScope>().Set(company.Key.TenantId, company.Key.CompanyId, null);
            var request = sp.GetRequiredService<RequestContext>();
            request.TenantId = company.Key.TenantId;
            request.CompanyId = company.Key.CompanyId;
            request.Language = "ar";
            var service = sp.GetRequiredService<PendingPostingService>();
            foreach (var item in company)
            {
                if (await service.RetryAsync(item.Id, ct).ConfigureAwait(false)) posted++;
            }
        }
        return posted;
    }

    internal static TimeSpan Backoff(int attempts) => TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Clamp(attempts, 0, 6))));

    [LoggerMessage(Level = LogLevel.Error, Message = "Pending posting retry run failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
