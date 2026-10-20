using DirectService.Transportation.Application.Platform;

namespace DirectService.Transportation.Web.Workers;

/// <summary>
/// Closes support windows at their end time (owner testing 20 Oct 2026, DT-220): an approved window whose end has passed becomes Expired with
/// an "Expire" row in the tenant's audit log, and the staff member's open pages leave the company at once (live message) and an email is
/// sent. Requests after the end are refused anyway (403 support.windowExpired); this job makes the open pages know without a new request.
/// Runs every 15 seconds when Jobs:Enabled is true.
/// </summary>
internal sealed partial class SupportWindowWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<SupportWindowWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunSafelyAsync(stoppingToken).ConfigureAwait(false);
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

    /// <summary>Closes every window past its end; returns how many.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var closed = await scope.ServiceProvider.GetRequiredService<SupportAccessService>().ExpireDueAsync(ct).ConfigureAwait(false);
        if (closed > 0) LogClosed(log, closed);
        return closed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Support windows: {Count} window(s) reached their end time and were closed.")]
    private static partial void LogClosed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Support window run failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
