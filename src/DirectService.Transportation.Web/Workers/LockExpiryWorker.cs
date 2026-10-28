using DirectService.Shared.Infrastructure.Collaboration;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Web.Workers;

/// <summary>
/// Releases record locks idle for more than 90 seconds (BR-039). The release is a soft delete attributed to
/// the lock holder (DeletedBy is required by the design). Runs every 30 s when Jobs:Enabled is true.
/// </summary>
internal sealed partial class LockExpiryWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<LockExpiryWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly string[] AllFilters = [QueryFilters.Tenant, QueryFilters.Company];

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
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
        var dataScope = scope.ServiceProvider.GetRequiredService<IDataScope>();
        var cutoff = clock.GetUtcNow().UtcDateTime - RecordLockService.Timeout;
        var expired = await db.Set<RecordLock>().IgnoreQueryFilters(AllFilters)
            .Where(l => l.LastActivityAt < cutoff).Take(500).ToListAsync(ct).ConfigureAwait(false);
        foreach (var l in expired)
        {
            using (dataScope.Use(l.TenantId, l.CompanyId, l.UserId))
            {
                l.DeleteReason = "Expired after 90 seconds without activity";
                db.Remove(l);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        return expired.Count;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Lock expiry run failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
