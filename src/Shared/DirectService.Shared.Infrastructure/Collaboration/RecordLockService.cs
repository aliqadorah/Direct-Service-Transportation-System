using Microsoft.EntityFrameworkCore;

namespace DirectService.Shared.Infrastructure.Collaboration;

public sealed record LockInfo(long LockId, Guid LockPublicId, string HolderName, bool HeldByMe, DateTime AcquiredAt);

/// <summary>
/// Record locks (BR-039, FR-186, ADR-11): opening a record for editing locks it; the client sends a
/// heartbeat every 30 s; the lock is released on close (soft delete, reason "Released") or after 90 s
/// without activity. The server enforces locks on every update (<see cref="IRecordLockGuard"/>). A shared engine (Stage 2 session 6, DT-312):
/// the holder's name comes through <see cref="IUserNames"/>.
/// </summary>
public sealed class RecordLockService(IAppDbContext db, IRequestContext ctx, IDataScope scope, IUserNames users, TimeProvider clock) : IRecordLockGuard
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Acquires (or refreshes) the lock; throws 423 when another user holds an active lock.</summary>
    public async Task<LockInfo> AcquireAsync(string entityType, long entityId, CancellationToken ct)
    {
        var me = ctx.UserId ?? throw Fail.Forbidden();
        var current = await db.Set<RecordLock>()
            .SingleOrDefaultAsync(l => l.EntityType == entityType && l.EntityId == entityId, ct).ConfigureAwait(false);
        if (current is not null)
        {
            if (current.UserId == me)
            {
                current.LastActivityAt = Now;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return await InfoAsync(current, ct).ConfigureAwait(false);
            }
            if (current.LastActivityAt + Timeout > Now)
                throw new AppException(SharedErrorCodes.LockHeld, 423, null, await HolderNameAsync(current.UserId, ct).ConfigureAwait(false));
            // Expired: release it on behalf of its holder (DeletedBy = holder) and take it over.
            using (scope.Use(current.TenantId, current.CompanyId, current.UserId))
            {
                current.DeleteReason = "Expired after 90 seconds without activity";
                db.Set<RecordLock>().Remove(current);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        var l = new RecordLock { EntityType = entityType, EntityId = entityId, UserId = me, AcquiredAt = Now, LastActivityAt = Now };
        db.Set<RecordLock>().Add(l);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another user acquired it at the same moment (UX_RecordLocks_CompanyId_EntityType_EntityId).
            db.ChangeTracker.Clear();
            var holder = await db.Set<RecordLock>().AsNoTracking()
                .Where(x => x.EntityType == entityType && x.EntityId == entityId).Select(x => x.UserId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            throw new AppException(SharedErrorCodes.LockHeld, 423, null, await HolderNameAsync(holder, ct).ConfigureAwait(false));
        }
        return await InfoAsync(l, ct).ConfigureAwait(false);
    }

    public async Task HeartbeatAsync(Guid lockPublicId, CancellationToken ct)
    {
        var l = await MineAsync(lockPublicId, ct).ConfigureAwait(false);
        l.LastActivityAt = Now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<RecordLock?> ReleaseAsync(Guid lockPublicId, CancellationToken ct)
    {
        var l = await db.Set<RecordLock>().SingleOrDefaultAsync(x => x.PublicId == lockPublicId, ct).ConfigureAwait(false);
        if (l is null) return null;
        if (l.UserId != ctx.UserId) throw Fail.Forbidden();
        l.DeleteReason = "Released";
        db.Set<RecordLock>().Remove(l);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return l;
    }

    public async Task EnsureNotLockedByOthersAsync(string entityType, long entityId, CancellationToken ct)
    {
        var l = await db.Set<RecordLock>().AsNoTracking()
            .Where(x => x.EntityType == entityType && x.EntityId == entityId)
            .Select(x => new { x.UserId, x.LastActivityAt }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (l is not null && l.UserId != ctx.UserId && l.LastActivityAt + Timeout > Now)
            throw new AppException(SharedErrorCodes.LockHeld, 423, null, await HolderNameAsync(l.UserId, ct).ConfigureAwait(false));
    }

    private async Task<RecordLock> MineAsync(Guid lockPublicId, CancellationToken ct)
    {
        var l = await db.Set<RecordLock>().SingleOrDefaultAsync(x => x.PublicId == lockPublicId, ct).ConfigureAwait(false)
            ?? throw Fail.NotFound();
        if (l.UserId != ctx.UserId) throw Fail.Forbidden();
        return l;
    }

    private async Task<LockInfo> InfoAsync(RecordLock l, CancellationToken ct) =>
        new(l.Id, l.PublicId, await HolderNameAsync(l.UserId, ct).ConfigureAwait(false), l.UserId == ctx.UserId, l.AcquiredAt);

    private Task<string> HolderNameAsync(long userId, CancellationToken ct) => users.FullNameAsync(userId, ct);
}
