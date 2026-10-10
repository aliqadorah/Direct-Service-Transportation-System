namespace DirectService.Transportation.Contracts.Collaboration;

// ------------------------------------------------------------------ global search (FR-187)

/// <summary>A search hit: record type (trip, invoice, driver …), its token, a title and a detail line.</summary>
public sealed record SearchHit(string Type, string Id, string Title, string? Detail);

/// <summary>Hits grouped by type, at most a few per type; only types the caller may view.</summary>
public sealed record SearchResponse(string Query, IReadOnlyList<SearchHit> Hits);

// ------------------------------------------------------------------ notifications (FR-190)

public sealed record NotificationResponse(string Id, string Type, string Message, string? Link, string? Company, DateTime CreatedAt, DateTime? ReadAt);

public sealed record NotificationPage(IReadOnlyList<NotificationResponse> Items, int Unread);

// ------------------------------------------------------------------ audit log (FR-188, FR-189)

public sealed record AuditLogQuery(string? User, string? Action, string? EntityType, DateOnly? From, DateOnly? To, string? Q, int Page = 1, int PageSize = 50);

/// <summary>A field change old → new.</summary>
public sealed record AuditChange(string Field, string? Old, string? New);

/// <summary>An audit row: time, user, action, record type and reference, reason, changes, IP address.</summary>
public sealed record AuditLogEntry(DateTime OccurredAt, string? User, string Action, string? EntityType, string? Record, string? Reason, IReadOnlyList<AuditChange> Changes,
    string? IpAddress);

// ------------------------------------------------------------------ background exports (FR-151)

/// <summary>A queued export: the token of the file once ready (sent with the notification).</summary>
public sealed record ExportJobResponse(string Id, string Status);
