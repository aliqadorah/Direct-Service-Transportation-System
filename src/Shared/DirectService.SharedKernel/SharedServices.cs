namespace DirectService.SharedKernel;

/// <summary>Hashed, entity-scoped opaque identifiers for URLs (SEC-22, ADR-07).</summary>
public interface IUrlTokenService
{
    /// <summary>22-character token for a record's PublicId, scoped to the entity type.</summary>
    string Encode<TEntity>(Guid publicId);

    /// <summary>The PublicId of a token, or null when the token is malformed or not of this entity type.</summary>
    Guid? Decode<TEntity>(string? token);

    /// <summary>Non-generic forms for code that knows the entity type only at run time.</summary>
    string Encode(Type entityType, Guid publicId);

    Guid? Decode(Type entityType, string? token);
}

/// <summary>Resolves a namespaced entity type (e.g. "TRN.Trip") and a URL token to the internal id, within the current scope.</summary>
public interface IEntityResolver
{
    /// <summary>The CLR type of a namespaced entity name, or null when unknown.</summary>
    Type? TypeOf(string entityType);

    /// <summary>Internal id of the record (respecting tenant/company/soft-delete filters), or null.</summary>
    Task<long?> ResolveIdAsync(string entityType, string token, CancellationToken ct);
}

/// <summary>Gap-free numbering inside the caller's transaction (BR-037, BR-038).</summary>
public interface INumberingService
{
    /// <summary>
    /// Takes the next number of a document type for the current company, holding an update lock on
    /// the sequence row until the caller's transaction ends. Throws when no transaction is active.
    /// </summary>
    Task<string> NextAsync(string documentType, CancellationToken ct);

    /// <summary>
    /// Takes the same update lock without issuing a number, so an operation (e.g. closing a period) waits for the
    /// documents being issued and blocks new ones until its transaction ends.
    /// </summary>
    Task LockAsync(string documentType, CancellationToken ct);
}

/// <summary>How a document number is written: prefix + the number padded with zeros to the sequence's digits (cfg.NumberSequences).</summary>
public static class DocumentNumbers
{
    public static string Format(string prefix, byte digits, long number) =>
        prefix + number.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');
}

/// <summary>Record locks (BR-039, FR-186).</summary>
public interface IRecordLockGuard
{
    /// <summary>Throws 423 when another user holds an active lock on the record.</summary>
    Task EnsureNotLockedByOthersAsync(string entityType, long entityId, CancellationToken ct);
}

/// <summary>Actions run once the current database transaction commits (none on rollback); at once when no transaction is open.</summary>
public interface IAfterCommit
{
    void Add(Func<Task> action);
}

/// <summary>Pushes changes to connected users of the same company (RT-01).</summary>
public interface IRealtimeNotifier
{
    Task RecordChangedAsync(long companyId, string entityType, string? token, string action, CancellationToken ct);

    /// <summary>A new in-app notification for one user (FR-190): pushed to that user's connections.</summary>
    Task NotifyUserAsync(Guid userPublicId, object notification, CancellationToken ct);
    Task LockChangedAsync(long companyId, string entityType, string token, string? holderName, CancellationToken ct);

    /// <summary>A support window of this staff member ended (subscriber or expiry, DT-220): pushed to the staff member's open pages.</summary>
    Task SupportEndedAsync(Guid staffPublicId, object payload, CancellationToken ct);
}

/// <summary>Stores generated files (exports) — provider-neutral; the development implementation writes to a local folder (DEF-01).</summary>
public interface IFileStore
{
    Task SaveAsync(string key, byte[] content, CancellationToken ct);

    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct);

    Task<byte[]?> ReadAsync(string key, CancellationToken ct);

    /// <summary>Removes files under the prefix older than the age; returns how many.</summary>
    Task<int> PurgeAsync(string prefix, TimeSpan age, CancellationToken ct);

    /// <summary>Total bytes of the files under the prefix (storage usage, P-07).</summary>
    Task<long> SizeAsync(string prefix, CancellationToken ct);
}
