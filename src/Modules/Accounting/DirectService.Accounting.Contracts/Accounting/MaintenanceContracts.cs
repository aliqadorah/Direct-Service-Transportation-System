namespace DirectService.Accounting.Contracts.Accounting;

/// <summary>The four maintenance actions (FR-135) as used in the routes.</summary>
public static class MaintenanceActions
{
    public const string DeleteEntries = "delete-entries";
    public const string DeleteAllEntries = "delete-all-entries";
    public const string ReplaceChart = "replace-chart";
    public const string Reset = "reset";
}

/// <summary>
/// A maintenance action (acct.maint): the typed confirmation word, the reason, the selected entries (delete-entries) and, for
/// replace-chart, what replaces the chart: "template" (the approved transport chart, D-10) or "empty" (import a chart afterwards).
/// </summary>
public sealed record MaintenanceRequest(string? Confirmation, string? Reason, IReadOnlyList<string>? EntryIds, string? ChartSource);

/// <summary>
/// What an action would do, before confirmation (FR-136): records affected per type, entries left alone because they are linked to
/// documents, the reasons the action is refused (closed periods, links), and the word to type.
/// </summary>
public sealed record MaintenancePreview(
    string Action, IReadOnlyDictionary<string, int> Counts, int LinkedEntriesKept, IReadOnlyList<string> Blockers, string ConfirmationWord);

public sealed record MaintenanceResult(string Action, IReadOnlyDictionary<string, int> Counts, DateTime OccurredAt);

/// <summary>A maintenance-log row: user, time, action, counts per record type (FR-136).</summary>
public sealed record MaintenanceLogEntry(DateTime OccurredAt, string? User, string Action, IReadOnlyDictionary<string, int> Counts);
