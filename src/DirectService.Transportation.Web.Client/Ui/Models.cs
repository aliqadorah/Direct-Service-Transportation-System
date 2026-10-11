using Microsoft.AspNetCore.Components;

namespace DirectService.Transportation.Web.Client.Ui;

/// <summary>A breadcrumb; the last one usually has no link.</summary>
public sealed record Crumb(string Text, string? Href = null);

/// <summary>A figure in a figures strip: label, value (already formatted), optional note.</summary>
public sealed record Figure(string Label, string Value, string? Note = null);

/// <summary>A label/value pair; Value is plain text (isolated runs) unless Content is given.</summary>
public sealed record KeyValue(string Key, string? Value, RenderFragment? Content = null);

/// <summary>A tab or saved view.</summary>
public sealed record TabItem(string Key, string Text, int? Count = null);

/// <summary>A history entry (FR-016 record history): who, what, when, optional change "old → new", void marker.</summary>
public sealed record HistoryEntry(string Who, string What, string When, string? OldValue = null, string? NewValue = null, bool IsVoid = false);

/// <summary>An option of a select or combo box: the stored value, the display text, an optional secondary text (code, plate…).</summary>
public sealed record Option<TValue>(TValue Value, string Text, string? Sub = null, bool Disabled = false);
