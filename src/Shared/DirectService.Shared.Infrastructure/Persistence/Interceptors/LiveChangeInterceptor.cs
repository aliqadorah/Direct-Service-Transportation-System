using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Shared.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Live updates of every list (FR-185, RT-01): business records of a company changed by a save are announced to that company's connected
/// users once the data is committed — after the save when no transaction is open, otherwise when the transaction commits (nothing on
/// rollback). Messages carry the record type, its URL token and the action, never internal ids or values; more than
/// <see cref="BulkThreshold"/> changes of one type in one commit become a single "Bulk" message (imports). Technical rows (locks,
/// sessions, audit) are not announced. A failure to publish never fails the business operation.
/// </summary>
public sealed class LiveChangeInterceptor(IServiceProvider services, IUrlTokenService tokens) : SaveChangesInterceptor, IDbTransactionInterceptor, IAfterCommit
{
    public const int BulkThreshold = 100;

    private readonly List<(long CompanyId, string EntityType, Type Clr, Guid PublicId, string Action)> _pending = [];
    private readonly List<Func<Task>> _after = [];
    private readonly Stack<(int Changes, int Actions)> _savepoints = new();
    private int _saveStart;
    private bool _inTransaction;

    /// <summary>Runs after the commit of the current transaction, or at once when none is open (IAfterCommit).</summary>
    public void Add(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_inTransaction) _after.Add(action);
        else _ = RunSafely(action);
    }

    private static async Task RunSafely(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort (RT-02: clients refresh on reconnect).
        }
    }

    private static bool Announced(object entity) => entity is AuditableEntity and ICompanyScoped and not INotAudited;   // not locks, sessions, notifications

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Capture(DbContext? context)
    {
        if (context is null) return;
        _saveStart = _pending.Count;
        _inTransaction = context.Database.CurrentTransaction is not null || _inTransaction;
        foreach (var e in context.ChangeTracker.Entries())
        {
            if (!Announced(e.Entity) || e.State is EntityState.Unchanged or EntityState.Detached) continue;
            var entity = (AuditableEntity)e.Entity;
            var action = e.State switch
            {
                EntityState.Added => "Created",
                EntityState.Deleted => "Deleted",
                _ => entity.IsDeleted ? "Deleted" : "Updated",
            };
            _pending.Add((((ICompanyScoped)e.Entity).CompanyId, AuditSaveChangesInterceptor.EntityTypeName(e.Entity), e.Entity.GetType(), entity.PublicId, action));
        }
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context?.Database.CurrentTransaction is null) await FlushAsync().ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A failed save captured changes that were never written: drop them (review M1).</summary>
    private void Failed()
    {
        if (_saveStart < _pending.Count) _pending.RemoveRange(_saveStart, _pending.Count - _saveStart);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Failed();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed();
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        _inTransaction = true;
        return result;
    }

    public ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        _inTransaction = true;
        return ValueTask.FromResult(result);
    }

    // Savepoints (posting engine): changes after a savepoint that is rolled back were never committed (review M1).
    public void CreatedSavepoint(DbTransaction transaction, TransactionEventData eventData) => _savepoints.Push((_pending.Count, _after.Count));

    public Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        CreatedSavepoint(transaction, eventData);
        return Task.CompletedTask;
    }

    public void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData)
    {
        if (_savepoints.Count == 0) return;
        var (changes, actions) = _savepoints.Pop();
        if (changes < _pending.Count) _pending.RemoveRange(changes, _pending.Count - changes);
        if (actions < _after.Count) _after.RemoveRange(actions, _after.Count - actions);
    }

    public Task RolledBackToSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        RolledBackToSavepoint(transaction, eventData);
        return Task.CompletedTask;
    }

    public void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData)
    {
        if (_savepoints.Count > 0) _savepoints.Pop();
    }

    public Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        ReleasedSavepoint(transaction, eventData);
        return Task.CompletedTask;
    }

    // After the commit with no cancellation and no exception: the data is saved whatever happens to the request (review M2).
    public async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        await FlushAsync().ConfigureAwait(false);

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => _ = FlushAsync();   // no blocking (review L5)

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Reset();
        return Task.CompletedTask;
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Reset();

    private void Reset()
    {
        _pending.Clear();
        _after.Clear();
        _savepoints.Clear();
        _inTransaction = false;
    }

    private async Task FlushAsync()
    {
        _inTransaction = false;
        _savepoints.Clear();
        var after = _after.ToList();
        _after.Clear();
        foreach (var a in after) await RunSafely(a).ConfigureAwait(false);
        if (_pending.Count == 0) return;
        var batch = _pending.ToList();
        _pending.Clear();
        var notifier = services.GetService<IRealtimeNotifier>();
        if (notifier is null) return;
        var ct = CancellationToken.None;
        try
        {
            foreach (var group in batch.GroupBy(c => (c.CompanyId, c.EntityType)))
            {
                var items = group.DistinctBy(c => c.PublicId).ToList();
                if (items.Count > BulkThreshold)
                {
                    await notifier.RecordChangedAsync(group.Key.CompanyId, group.Key.EntityType, null, "Bulk", ct).ConfigureAwait(false);
                    continue;
                }
                foreach (var c in items)
                    await notifier.RecordChangedAsync(c.CompanyId, c.EntityType, tokens.Encode(c.Clr, c.PublicId), c.Action, ct).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Live updates are best effort: clients refresh on reconnect (RT-02).
        }
    }


}
