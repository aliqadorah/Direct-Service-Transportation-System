using DirectService.Transportation.Infrastructure.Email;

namespace DirectService.Transportation.Web.Workers;

/// <summary>
/// Sends the outbox (DT-251): woken when an email is queued, otherwise every 10 seconds (retries come due, the per-minute limit frees up).
/// Each run claims its rows, so several hosts can run it side by side; see <see cref="EmailDispatcher"/>. Runs when Jobs:Enabled is true.
/// </summary>
internal sealed partial class EmailOutboxWorker(IServiceScopeFactory scopes, EmailOutboxSignal signal, ILogger<EmailOutboxWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<EmailDispatcher>().RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(log, ex);
            }
            await signal.WaitAsync(Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Email outbox run failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
