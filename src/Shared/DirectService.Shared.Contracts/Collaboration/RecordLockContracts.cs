namespace DirectService.Shared.Contracts;

// Record locks (FR-186): the shared engine in Shared.Infrastructure (Stage 2 session 6, DT-312).

public sealed record AcquireLockRequest(string EntityType, string Id);
public sealed record LockResponse(string Id, string HolderName, bool HeldByMe, DateTime AcquiredAt);
