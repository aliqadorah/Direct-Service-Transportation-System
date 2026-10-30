using DirectService.Platform.Application.Identity;

namespace DirectService.Transportation.Web.Workers;

/// <summary>
/// Ends sessions past their absolute lifetime (12 h) or idle longer than the tenant's timeout (owner testing 19 Oct 2026, DT-211): they
/// were already refused on every request, but stayed listed as "active" under Profile → Active sessions. Runs every 10 minutes when
/// Jobs:Enabled is true, in batches of 1,000.
/// </summary>
internal sealed partial class SessionCleanupWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SessionCleanupWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private const int Batch = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunSafelyAsync(stoppingToken).ConfigureAwait(false);   // once at start-up, then every interval
        using var timer = new PeriodicTimer(Interval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            await RunSafelyAsync(stoppingToken).ConfigureAwait(false);
    }

    private async Task RunSafelyAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(log, ex);
        }
    }

    /// <summary>Ends every expired session (all batches); returns how many.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var ended = await scope.ServiceProvider.GetRequiredService<SessionService>().EndExpiredAsync(Batch, ct).ConfigureAwait(false);
            total += ended;
            if (ended < Batch) break;
        }
        if (total > 0) LogEnded(log, total);
        return total;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Session clean-up: {Count} expired session(s) ended.")]
    private static partial void LogEnded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Session clean-up run failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
