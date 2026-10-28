namespace DirectService.SharedKernel;

/// <summary>
/// Per-operation information the audit interceptor cannot infer: the business action name
/// (Void, Approve, Archive…) and the reason for a soft delete.
/// </summary>
public interface IChangeContext
{
    /// <summary>Records the audit action (and optional reason) for an entity changed in the current unit of work.</summary>
    void SetAction(object entity, string action, string? reason = null);

    /// <summary>The action previously set for the entity, if any.</summary>
    string? GetAction(object entity);

    /// <summary>The reason recorded with the action, if any (written to the audit log).</summary>
    string? GetReason(object entity);

    /// <summary>Clears per-save state after a successful save.</summary>
    void Reset();
}
