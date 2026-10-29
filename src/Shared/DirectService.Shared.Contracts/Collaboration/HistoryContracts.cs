namespace DirectService.Shared.Contracts;

// The readable history (owner feedback 14 Oct 2026, DT-188): the parts of an event the shared audit describer builds (Stage 2 session 9).

/// <summary>One change of a field in a history entry (old → new, FR-166): the field's label and both values in the reader's language (DT-188).</summary>
public sealed record FieldChange(string Field, string? OldValue, string? NewValue);

/// <summary>A journal line shown with its entry's event: account "code · name", debit and credit formatted (empty when zero).</summary>
public sealed record HistoryLine(string Account, string? Debit, string? Credit);

/// <summary>A document named by a history event: its screen kind ("Invoice", "Settlement", "JournalEntry"…), token and number.</summary>
public sealed record HistoryLink(string Kind, string Id, string Text);
