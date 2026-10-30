namespace DirectService.Platform.Application.Notifications;

/// <summary>The export notifications (CORE.ExportReady with the file name and link, CORE.ExportFailed) as in-app notifications (Platform since Stage 2 session 14).</summary>
public sealed class ExportNotifications(NotificationService notifications) : IExportNotifier
{
    public Task ReadyAsync(ExportJob job, string fileName, string link, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        return notifications.NotifyAsync([job.UserId], job.CompanyId, NotificationService.ExportReady, new Dictionary<string, string?> { ["file"] = fileName }, link, ct);
    }

    public Task FailedAsync(ExportJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        return notifications.NotifyAsync([job.UserId], job.CompanyId, NotificationService.ExportFailed, new Dictionary<string, string?>(), null, ct);
    }
}
