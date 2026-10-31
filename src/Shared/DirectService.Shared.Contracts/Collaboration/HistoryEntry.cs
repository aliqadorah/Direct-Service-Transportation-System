namespace DirectService.Shared.Contracts.History;

// Its own namespace (Stage 3 session 6c): Shared.UI has a view model of the same name (DirectService.Shared.UI.Ui.HistoryEntry), and the UI
// projects import DirectService.Shared.Contracts globally.

/// <summary>
/// A history entry of a record (FR-166), readable (owner feedback 14 Oct 2026, DT-188): <paramref name="Text"/> is the event as a sentence in
/// the reader's language ("Ali Ali added the trip to draft invoice INV-00001"), <paramref name="ActionLabel"/> the action in words, the
/// changes with field labels and formatted values (technical columns left out), <paramref name="Lines"/> the lines of a journal entry posted
/// with it and <paramref name="Links"/> the documents it names (the screen opens them when the reader may see them).
/// </summary>
public sealed record HistoryEntry(DateTime At, string? User, string Action, string? Reason, IReadOnlyList<FieldChange> Changes,
    string? Text = null, string? ActionLabel = null, IReadOnlyList<HistoryLine>? Lines = null, IReadOnlyList<HistoryLink>? Links = null);
