using DirectService.Transportation.Application.Platform;
using DirectService.Platform.Application.Tenants;

namespace DirectService.Transportation.Web.Workers;

/// <summary>Runs the tenant lifecycle (5.2, G-07, G-18) every hour: trial ends, renewals, grace, cancellation, deletion after retention.</summary>
internal sealed partial class TenantLifecycleWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TenantLifecycleWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        do
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
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TenantLifecycleService>().RunAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Tenant lifecycle run failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
