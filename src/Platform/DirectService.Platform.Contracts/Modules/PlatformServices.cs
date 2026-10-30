namespace DirectService.Platform.Contracts.Modules;

// The Platform services a module calls (platform design a.2: a module's Application references Platform.Contracts, never Platform.Application;
// Stage 2 session 14). Implemented and registered by Platform.Application. Browser-safe: plain values only.

/// <summary>The plan limits a module checks before it saves (BR-012).</summary>
public interface IPlanLimitCheck
{
    /// <summary><paramref name="adding"/> more trips this month must fit the plan's MaxTripsPerMonth ("trips saved per calendar month").</summary>
    Task EnsureTripsAsync(long tenantId, int adding, CancellationToken ct);
}
