namespace DirectService.Transportation.Contracts.Collaboration;

// ------------------------------------------------------------------ notifications (FR-190)

/// <summary>
/// A notification in the reader's language (DT-189): title, one readable sentence, technical details apart (shown on request). Link is the
/// screen to open — relative to the company named by Company (its URL token), or absolute for tenant screens; LinkProblem says why there
/// is none ("missing": the record no longer exists; "forbidden": the reader may not open it). CompanyName is for display only.
/// </summary>
public sealed record NotificationResponse(string Id, string Type, string Title, string Message, string? Details, string? Link, string? LinkProblem, string? Company,
    string? CompanyName, DateTime CreatedAt, DateTime? ReadAt);

public sealed record NotificationPage(IReadOnlyList<NotificationResponse> Items, int Unread);

// ------------------------------------------------------------------ audit log (FR-188, FR-189)

/// <param name="Technical">Adds the stored values as they are (administrators with settings.manage only, DT-188).</param>
public sealed record AuditLogQuery(string? User, string? Action, string? EntityType, DateOnly? From, DateOnly? To, string? Q, int Page = 1, int PageSize = 50,
    bool Technical = false);

/// <summary>A field change old → new.</summary>
public sealed record AuditChange(string Field, string? Old, string? New);

/// <summary>
/// An audit row: time, user, action, record type and reference, reason, changes, IP address. Readable (DT-188): the action and record type
/// in words, the event as a sentence, changes with labels and formatted values, a journal entry's lines and the documents it names;
/// <paramref name="Technical"/> holds the stored values as they are, only when an administrator asks for the technical details.
/// </summary>
public sealed record AuditLogEntry(DateTime OccurredAt, string? User, string Action, string? EntityType, string? Record, string? Reason, IReadOnlyList<AuditChange> Changes,
    string? IpAddress, string? ActionLabel = null, string? EntityLabel = null, string? Text = null,
    IReadOnlyList<MasterData.HistoryLine>? Lines = null, IReadOnlyList<MasterData.HistoryLink>? Links = null, IReadOnlyList<AuditChange>? Technical = null);

// ------------------------------------------------------------------ background exports (FR-151)

/// <summary>A queued export: the token of the file once ready (sent with the notification).</summary>
public sealed record ExportJobResponse(string Id, string Status);
