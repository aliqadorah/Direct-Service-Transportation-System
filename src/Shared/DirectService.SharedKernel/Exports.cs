using DirectService.Shared.Contracts;

namespace DirectService.SharedKernel;

// Background exports (FR-151): the engine (ExportService, the channel queue) in Shared.Infrastructure; the module kinds through
// IExportJobProvider, the notifications through IExportNotifier (Stage 2 session 10, DT-334).

/// <summary>A queued export with the requester's identity (the worker runs it with the requester's permissions).</summary>
public sealed record ExportJob(Guid Id, long TenantId, long CompanyId, long UserId, Guid UserPublicId, string? UserName, string Language, ExportRequest Request);

/// <summary>The in-process queue of export jobs (a channel; the jobs table is not part of the design).</summary>
public interface IExportQueue
{
    bool Enqueue(ExportJob job);

    IAsyncEnumerable<ExportJob> ReadAllAsync(CancellationToken ct);

    bool TryRead(out ExportJob? job);
}

/// <summary>Where export files are kept in the file store (pinned path format, platform design §e, E-33) and for how long.</summary>
public static class ExportFiles
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    /// <summary>Root folder of export files in the file store.</summary>
    public const string Root = "exports/";

    /// <summary>The requester's folder: <c>exports/{tenant}/{company}/{userPid:N}/</c> (each job in a sub-folder of its id).</summary>
    public static string Folder(long tenantId, long companyId, Guid userPid) => $"{Root}{tenantId}/{companyId}/{userPid:N}/";
}

/// <summary>
/// The export kinds a module serves (e.g. standard reports, the audit log, the company copy): checked at once with the caller's permissions,
/// then run by the worker with the requester's permissions.
/// </summary>
public interface IExportJobProvider
{
    /// <summary>The kinds, lower case, as the request names them.</summary>
    IReadOnlyList<string> Kinds { get; }

    /// <summary>Refuses (throws) a request the caller may not make; called before the job is queued.</summary>
    Task CheckAsync(string kind, ExportRequest r, CancellationToken ct);

    Task<GeneratedFile> RunAsync(string kind, ExportRequest r, CancellationToken ct);
}

/// <summary>Tells the requester that an export is ready (with its link) or failed (in-app notifications of the platform).</summary>
public interface IExportNotifier
{
    Task ReadyAsync(ExportJob job, string fileName, string link, CancellationToken ct);

    Task FailedAsync(ExportJob job, CancellationToken ct);
}

/// <summary>Set by the application: the permission needed to export a report-builder definition or a saved report (reports.view).</summary>
public sealed class ExportOptions
{
    public string ReportPermission { get; init; } = string.Empty;
}
