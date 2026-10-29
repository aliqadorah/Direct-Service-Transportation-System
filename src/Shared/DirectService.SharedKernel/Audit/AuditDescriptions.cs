using System.Text.Json;
using DirectService.Shared.Contracts;
using static DirectService.SharedKernel.AuditWords;

namespace DirectService.SharedKernel;

/// <summary>An audit row as stored (core.AuditLogs), the input of <see cref="IAuditDescriber"/>.</summary>
public sealed record AuditRow(long Id, DateTime OccurredAt, long? UserId, string Action, string? EntityType, long? EntityId, string? OldValues, string? NewValues);

/// <summary>
/// An audit row in the reader's words: who, the action and record type in words, the record by its number or name, the event as a sentence
/// (<see cref="Sentence"/> with the user, <see cref="Predicate"/> without), the reason, the changes (labels, formatted values, technical
/// columns left out), the lines of a journal entry posted with it and the documents it names.
/// </summary>
public sealed record AuditDescription(AuditRow Row, string? User, string ActionLabel, string EntityLabel, string? Record, string Sentence, string Predicate,
    string? Reason, IReadOnlyList<FieldChange> Changes, IReadOnlyList<HistoryLine> Lines, IReadOnlyList<HistoryLink> Links)
{
    /// <summary>An edit that changed only technical columns (e.g. the journal link set by a posting): not a business event of the record.</summary>
    public bool IsTechnicalOnly => Row.Action == "Update" && Changes.Count == 0 && Reason is null && Lines.Count == 0;
}

/// <summary>Options of a description run.</summary>
/// <param name="CreateValues">Show the values of a created record as changes (audit log); the record's own history does not repeat them.</param>
/// <param name="LinkRecord">Add a link to the record itself (audit log); a record's history is on its own screen.</param>
public sealed record DescribeOptions(bool CreateValues = false, bool LinkRecord = false);

/// <summary>
/// The central audit describer (owner feedback 14 Oct 2026, DT-188): turns audit rows into readable events in the reader's language for
/// every History panel and the audit log (screen and exports). A shared engine; the words and the rules of each module come from its
/// <see cref="IAuditVocabularyProvider"/> (Stage 2 session 9).
/// </summary>
public interface IAuditDescriber
{
    Task<IReadOnlyList<AuditDescription>> DescribeAsync(IReadOnlyList<AuditRow> rows, DescribeOptions options, CancellationToken ct);
}

/// <summary>An audit row with its old and new values read (the stored JSON objects).</summary>
public sealed record ParsedAuditRow(AuditRow Row, IReadOnlyDictionary<string, JsonElement> Old, IReadOnlyDictionary<string, JsonElement> New)
{
    public JsonElement? Value(string column) => New.TryGetValue(column, out var v) ? v : Old.TryGetValue(column, out var o) ? o : null;

    public long? Whole(string column, bool old = false) =>
        (old ? Old : New).TryGetValue(column, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    public string? Str(string column) => Value(column) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public string? Reason => New.TryGetValue("_reason", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>What a provider's rules can read from a description run: the reader's language and the records the run found.</summary>
public interface IAuditRun
{
    bool En { get; }

    /// <summary>The name of a record the run found (in the reader's language), null when not found.</summary>
    string? Name(string type, long id);

    /// <summary>A link to a record the run found, null when it has no screen; <paramref name="text"/> defaults to its name.</summary>
    HistoryLink? Link(string type, long id, string? text = null);
}

/// <summary>The extra parts of an action's sentence a module adds: {x}, {d} (when set), a link, and whether the reason is told already.</summary>
public sealed record AuditActionDetail(string Extra = "", string? Document = null, bool DropReason = false, HistoryLink? Link = null);

/// <summary>Records whose lines are written as their own audit rows and shown inside the record's event (a journal entry and its lines).</summary>
/// <param name="EntryType">The record type of the entry (its created rows carry "Number").</param>
/// <param name="LineType">The record type of its lines.</param>
/// <param name="EntryColumn">The line's column holding the entry's id.</param>
/// <param name="OrderColumn">The line's column giving the order.</param>
public sealed record AuditLines(string EntryType, string LineType, string EntryColumn, string OrderColumn);

/// <summary>
/// A module's words and rules for the shared audit describer (Stage 2 session 9): its record types, actions, field labels, stored codes and
/// technical columns, and the hooks for the rules only that module knows. A hook returns null (or false) when the rule does not apply; the
/// describer then uses the common rule.
/// </summary>
public interface IAuditVocabularyProvider
{
    IReadOnlyDictionary<string, EntityWords> Entities { get; }

    IReadOnlyDictionary<string, ActionWords> Actions { get; }

    /// <summary>Field labels by column name.</summary>
    IReadOnlyDictionary<string, FieldWords> Fields { get; }

    /// <summary>Field labels by "type.column", before <see cref="Fields"/>.</summary>
    IReadOnlyDictionary<string, FieldWords> EntityFields { get; }

    IReadOnlyDictionary<string, Words> Codes { get; }

    /// <summary>Technical columns never shown to readers.</summary>
    IReadOnlySet<string> Hidden { get; }

    /// <summary>Personal fields per record type, masked once the record is erased (SEC-19, <see cref="ErasedRecords"/>).</summary>
    IReadOnlyDictionary<string, IReadOnlySet<string>> PersonalFields { get; }

    /// <summary>The entry and line types shown as one event, null when the module has none.</summary>
    AuditLines? Lines { get; }

    /// <summary>The screen kind of a record for links; <paramref name="record"/> is the loaded record when there is one.</summary>
    string? LinkKind(string entityType, object? record);

    /// <summary>The vocabulary key of an action on a record type (an action with words of its own on that type), null for the action itself.</summary>
    string? ActionKey(string action, string? entityType);

    /// <summary>The record type an id column points at when the vocabulary's reference is not the answer; <paramref name="target"/> null: not a reference.</summary>
    bool TryTarget(string? entityType, string column, ParsedAuditRow row, out string? target);

    /// <summary>Other records a row names (read with the others of the run).</summary>
    IEnumerable<(string Type, long? Id)> Related(ParsedAuditRow row);

    /// <summary>The record a row is named by instead of its own (a link row by the user or role it concerns).</summary>
    (string Type, long Id)? NamedBy(ParsedAuditRow row);

    /// <summary>The document an action names (reason holds its number, or the row its id): type, id and number.</summary>
    (string Type, long? Id, string? Number)? DocumentOf(ParsedAuditRow row);

    /// <summary>Whether an action tells its reason in the sentence already.</summary>
    bool TellsReason(string action);

    /// <summary>Columns an action already tells in its sentence (e.g. the invoice of a trip added to it).</summary>
    IReadOnlyCollection<string> ToldBy(string action);

    /// <summary>The words of the document an action names; <paramref name="number"/> its number or name, <paramref name="found"/> whether it was found.</summary>
    string? DocumentPhrase(string action, string type, string? number, bool found, bool en);

    /// <summary>The extra parts of the action's sentence.</summary>
    AuditActionDetail? Detail(ParsedAuditRow row, IAuditRun run);

    /// <summary>A part of a reason in the reader's words (a reason's codes are translated by the describer).</summary>
    string? ReasonPart(string part, bool en);

    /// <summary>Whether a column of a record type is never shown.</summary>
    bool HidesColumn(string? entityType, string column);

    /// <summary>Whether a column's values are shown masked to this reader.</summary>
    bool Masks(string? entityType, string column);

    /// <summary>How a column's value is shown when it differs from the vocabulary and the column's type.</summary>
    Kind? ValueKind(string? entityType, string column);

    /// <summary>A stored code of a column in words.</summary>
    string? CodeText(string? entityType, string column, string code, bool en);

    /// <summary>A text value in words (e.g. a setting's "true").</summary>
    string? TextValue(string? entityType, ParsedAuditRow row, string text, bool en);

    /// <summary>The name of a record from its values; <paramref name="loaded"/>: read now (else from the values the log recorded).</summary>
    bool TryRecordName(string entityType, Func<string, string?> value, bool loaded, bool en, out string? name);

    /// <summary>A line of an entry (<see cref="Lines"/>).</summary>
    HistoryLine Line(ParsedAuditRow line, IAuditRun run);

    /// <summary>Whether records of a type are read with every query filter lifted (names only, of ids found in the log).</summary>
    bool ReadsUnfiltered(string entityType);

    /// <summary>Whether a record of a type not found is named by the last name the log recorded.</summary>
    bool KeepsLastName(string entityType);
}
