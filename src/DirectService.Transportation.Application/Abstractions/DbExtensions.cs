using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Abstractions;

public static class DbExtensions
{
    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction (NFR-04): commits on success, rolls back
    /// on any exception. Joins an already active transaction.
    /// </summary>
    public static async Task<T> InTransactionAsync<T>(this IAppDbContext db, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(work);
        if (db.Database.CurrentTransaction is not null)
            return await work(ct).ConfigureAwait(false);

        await using var tx = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var result = await work(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return result;
    }

    public static Task InTransactionAsync(this IAppDbContext db, Func<CancellationToken, Task> work, CancellationToken ct) =>
        db.InTransactionAsync(async c => { await work(c).ConfigureAwait(false); return true; }, ct);

    /// <summary>
    /// Applies the RowVersion the client last saw so a stale update is refused (409). The record is always
    /// updated (even when only its child rows change), so its RowVersion advances on every edit.
    /// </summary>
    public static void ExpectRowVersion<TEntity>(this IAppDbContext db, TEntity entity, string? rowVersion) where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrEmpty(rowVersion)) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw Fail.Field("rowVersion", Contracts.Errors.ErrorCodes.ValidationFormat); }
        var entry = db.Set<TEntity>().Entry(entity);
        entry.Property("RowVersion").OriginalValue = bytes;
        if (entry.State == Microsoft.EntityFrameworkCore.EntityState.Unchanged)
            entry.State = Microsoft.EntityFrameworkCore.EntityState.Modified;
    }
}
