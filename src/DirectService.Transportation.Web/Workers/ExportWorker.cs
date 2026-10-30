using DirectService.Transportation.Application.Abstractions;
using DirectService.Shared.Infrastructure.Collaboration;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Platform.Application.Identity;

namespace DirectService.Transportation.Web.Workers;

/// <summary>
/// Runs queued exports (FR-151): each job in its own scope with the requester's tenant, company, language and current permissions in that
/// company (a user who lost access since gets nothing), the background limits, and a notification when the file is ready or failed. Files
/// older than seven days are removed on each pass.
/// </summary>
internal sealed partial class ExportWorker(IServiceScopeFactory scopes, IExportQueue queue, ILogger<ExportWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunAsync(job, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Runs every job waiting now (tests, and hosts without background jobs).</summary>
    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var count = 0;
        while (queue.TryRead(out var job) && job is not null)
        {
            await RunAsync(job, ct).ConfigureAwait(false);
            count++;
        }
        return count;
    }

    private async Task RunAsync(ExportJob job, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        try
        {
            var grant = (await sp.GetRequiredService<AccessService>().GrantsAsync(job.UserId, ct).ConfigureAwait(false))
                .FirstOrDefault(g => g.CompanyId == job.CompanyId && g.CompanyStatus == "Active");
            sp.GetRequiredService<DataScope>().Set(job.TenantId, job.CompanyId, job.UserId);
            var request = sp.GetRequiredService<RequestContext>();
            request.TenantId = job.TenantId;
            request.CompanyId = job.CompanyId;
            request.UserId = job.UserId;
            request.UserPublicId = job.UserPublicId;
            request.UserName = job.UserName;
            request.Language = job.Language;
            request.Permissions = grant?.Permissions ?? new HashSet<string>();
            sp.GetRequiredService<ExportLimits>().Background = true;
            var exports = sp.GetRequiredService<ExportService>();
            if (grant is null)
            {
                await exports.FailedAsync(job, ct).ConfigureAwait(false);
                return;
            }
            try
            {
                await exports.RunAsync(job, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogJobFailed(log, job.Id, ex);
                await exports.FailedAsync(job, ct).ConfigureAwait(false);
            }
            await sp.GetRequiredService<IFileStore>().PurgeAsync(ExportService.Root, ExportService.Keep, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogJobFailed(log, job.Id, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Export {JobId} failed.")]
    private static partial void LogJobFailed(ILogger logger, Guid jobId, Exception exception);
}
