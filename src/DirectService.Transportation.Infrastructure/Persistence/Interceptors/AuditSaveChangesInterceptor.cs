using System.Runtime.CompilerServices;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using DirectService.Transportation.Domain.Cfg;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace DirectService.Transportation.Infrastructure.Persistence.Interceptors;

/// <summary>
/// One interceptor for the rules every save must follow (build prompt "Data access"):
/// <list type="number">
/// <item>Soft delete — a delete becomes IsDeleted = 1 with DeletedAt/DeletedBy/DeleteReason; never a DELETE (BR-041).</item>
/// <item>Append-only guard — core.AuditLogs, core.MaintenanceLogs and trn.TRN_RateHistory are never updated or deleted.</item>
/// <item>Stamping — TenantId/CompanyId from the server scope, CreatedAt/By, UpdatedAt/By; tenant/company/creation columns cannot change.</item>
/// <item>Audit — one core.AuditLogs row per changed row with old → new values (FR-188), in the same transaction.</item>
/// </list>
/// Audit rows need the identity values of inserted rows, so they are saved right after the main
/// save inside the same transaction. If the caller has not started a transaction, one is started here.
/// </summary>
public sealed class AuditSaveChangesInterceptor(IDataScope scope, IChangeContext changes, TimeProvider clock)
    : SaveChangesInterceptor
{
    /// <summary>Columns never written to the audit log (secrets and binary content).</summary>
    private static readonly HashSet<string> MaskedColumns = new(StringComparer.Ordinal)
    {
        "PasswordHash", "SecurityStamp", "TwoFactorSecret", "CodeHash", "TokenHash",
        "ClientIdEncrypted", "SecretKeyEncrypted",
    };

    /// <summary>High-frequency technical rows whose changes are not business events.</summary>
    private static readonly HashSet<Type> NotAudited = [typeof(UserSession), typeof(RecordLock), typeof(AuditLog)];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private sealed class State
    {
        public List<(EntityEntry Entry, string Action, Dictionary<string, object?>? Old, Dictionary<string, object?>? New)> Pending { get; } = [];
        public IDbContextTransaction? OwnedTransaction { get; set; }
        public bool WritingAudit { get; set; }
    }

    private readonly ConditionalWeakTable<DbContext, State> _states = [];

    private State For(DbContext ctx) => _states.GetValue(ctx, _ => new State());

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context!;
        var state = For(ctx);
        if (state.WritingAudit) return result;
        if (ctx.Database.CurrentTransaction is null)
            state.OwnedTransaction = await ctx.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Prepare(ctx, state);
        }
        catch
        {
            await RollbackAsync(state).ConfigureAwait(false);
            throw;
        }
        return result;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        var ctx = eventData.Context!;
        var state = For(ctx);
        if (state.WritingAudit) return result;
        if (ctx.Database.CurrentTransaction is null)
            state.OwnedTransaction = ctx.Database.BeginTransaction();
        try
        {
            Prepare(ctx, state);
        }
        catch
        {
            Rollback(state);
            throw;
        }
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context!;
        var state = For(ctx);
        if (state.WritingAudit) return result;
        try
        {
            if (AddAuditRows(ctx, state))
            {
                state.WritingAudit = true;
                await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            if (state.OwnedTransaction is { } tx)
            {
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                await tx.DisposeAsync().ConfigureAwait(false);
                state.OwnedTransaction = null;
            }
        }
        catch
        {
            await RollbackAsync(state).ConfigureAwait(false);
            throw;
        }
        finally
        {
            state.WritingAudit = false;
            state.Pending.Clear();
            changes.Reset();
        }
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        var ctx = eventData.Context!;
        var state = For(ctx);
        if (state.WritingAudit) return result;
        try
        {
            if (AddAuditRows(ctx, state))
            {
                state.WritingAudit = true;
                ctx.SaveChanges();
            }
            if (state.OwnedTransaction is { } tx)
            {
                tx.Commit();
                tx.Dispose();
                state.OwnedTransaction = null;
            }
        }
        catch
        {
            Rollback(state);
            throw;
        }
        finally
        {
            state.WritingAudit = false;
            state.Pending.Clear();
            changes.Reset();
        }
        return result;
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        var state = For(eventData.Context!);
        if (!state.WritingAudit)
        {
            state.Pending.Clear();
            await RollbackAsync(state).ConfigureAwait(false);
        }
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        var state = For(eventData.Context!);
        if (!state.WritingAudit)
        {
            state.Pending.Clear();
            Rollback(state);
        }
    }

    private static async Task RollbackAsync(State state)
    {
        if (state.OwnedTransaction is { } tx)
        {
            state.OwnedTransaction = null;
            await tx.RollbackAsync().ConfigureAwait(false);
            await tx.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void Rollback(State state)
    {
        if (state.OwnedTransaction is { } tx)
        {
            state.OwnedTransaction = null;
            tx.Rollback();
            tx.Dispose();
        }
    }

    // ------------------------------------------------------------------ prepare

    private void Prepare(DbContext ctx, State state)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        state.Pending.Clear();

        foreach (var entry in ctx.ChangeTracker.Entries().ToList())
        {
            var entity = entry.Entity;
            var isAppendOnly = entity is IAppendOnly or RateHistory;
            switch (entry.State)
            {
                case EntityState.Deleted:
                    if (isAppendOnly || entity is not ISoftDeletable sd)
                        throw new InvalidOperationException($"{EntityTypeName(entity)} rows can never be deleted.");
                    if (string.IsNullOrWhiteSpace(sd.DeleteReason))
                        throw new InvalidOperationException($"A reason is required to delete a {EntityTypeName(entity)} record.");
                    var deletedBy = scope.UserId
                        ?? throw new InvalidOperationException("A soft delete needs the acting user (DeletedBy is required).");
                    entry.State = EntityState.Unchanged;
                    entry.Property(nameof(ISoftDeletable.IsDeleted)).CurrentValue = true;
                    entry.Property(nameof(ISoftDeletable.DeletedAt)).CurrentValue = now;
                    entry.Property(nameof(ISoftDeletable.DeletedBy)).CurrentValue = deletedBy;
                    entry.Property(nameof(ISoftDeletable.DeleteReason)).IsModified = true;
                    StampModified(entry, now);
                    Track(entry, changes.GetAction(entity) ?? "Delete", state);
                    break;

                case EntityState.Added:
                    StampAdded(entry, now);
                    Track(entry, changes.GetAction(entity) ?? "Create", state);
                    break;

                case EntityState.Modified:
                    if (isAppendOnly)
                        throw new InvalidOperationException($"{EntityTypeName(entity)} rows are append-only and can never be changed.");
                    GuardScopeColumns(entry);
                    StampModified(entry, now);
                    var action = changes.GetAction(entity);
                    if (action is null && entity is ISoftDeletable { IsDeleted: false }
                        && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified)
                        action = "Restore";
                    Track(entry, action ?? "Update", state);
                    break;
            }
        }
    }

    private void StampAdded(EntityEntry entry, DateTime now)
    {
        var entity = entry.Entity;
        if (entity is ITenantScoped t)
        {
            if (scope.TenantId is long st)
            {
                if (t.TenantId != 0 && t.TenantId != st)
                    throw new InvalidOperationException("Cannot create a record in another tenant.");
                t.TenantId = st;
            }
            else if (t.TenantId == 0)
            {
                throw new InvalidOperationException($"{EntityTypeName(entity)}: TenantId must come from the server scope.");
            }
        }
        if (entity is INullableTenantScoped nt && scope.TenantId is long nst)
        {
            if (nt.TenantId is long existing && existing != nst)
                throw new InvalidOperationException("Cannot create a record in another tenant.");
            nt.TenantId = nst;
        }
        if (entity is ICompanyScoped c)
        {
            if (scope.CompanyId is long sc)
            {
                if (c.CompanyId != 0 && c.CompanyId != sc)
                    throw new InvalidOperationException("Cannot create a record in another company.");
                c.CompanyId = sc;
            }
            else if (c.CompanyId == 0)
            {
                throw new InvalidOperationException($"{EntityTypeName(entity)}: CompanyId must come from the server scope.");
            }
        }
        if (entity is AuditableEntity a)
        {
            if (a.PublicId == Guid.Empty) a.PublicId = Guid.NewGuid();
            a.CreatedAt = now;
            a.CreatedBy ??= scope.UserId;
            a.UpdatedAt = null;
            a.UpdatedBy = null;
        }
    }

    private void StampModified(EntityEntry entry, DateTime now)
    {
        if (entry.Entity is not AuditableEntity a) return;
        a.UpdatedAt = now;
        a.UpdatedBy = scope.UserId;
        entry.Property(nameof(AuditableEntity.CreatedAt)).IsModified = false;
        entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
        entry.Property(nameof(AuditableEntity.PublicId)).IsModified = false;
    }

    private void GuardScopeColumns(EntityEntry entry)
    {
        foreach (var column in new[] { "TenantId", "CompanyId" })
        {
            var p = entry.Properties.FirstOrDefault(x => x.Metadata.Name == column);
            if (p is { IsModified: true } && !Equals(p.OriginalValue, p.CurrentValue))
                throw new InvalidOperationException($"{column} of an existing record can never change.");
        }
        if (scope.TenantId is long st)
        {
            var tenant = entry.Entity switch
            {
                ITenantScoped t => (long?)t.TenantId,
                INullableTenantScoped n => n.TenantId,
                _ => null,
            };
            if (tenant is long et && et != st)
                throw new InvalidOperationException("Cannot change a record of another tenant.");
        }
        if (scope.CompanyId is long sc && entry.Entity is ICompanyScoped c && c.CompanyId != sc)
            throw new InvalidOperationException("Cannot change a record of another company.");
    }

    // ------------------------------------------------------------------ audit

    private void Track(EntityEntry entry, string action, State state)
    {
        var reason = changes.GetReason(entry.Entity);
        if (NotAudited.Contains(entry.Entity.GetType()) || entry.Entity is IAppendOnly) return;
        Dictionary<string, object?>? oldValues = null;
        Dictionary<string, object?> newValues = [];
        foreach (var p in entry.Properties)
        {
            var name = p.Metadata.Name;
            if (name is "RowVersion" || p.Metadata.ClrType == typeof(byte[]) && !MaskedColumns.Contains(name)) continue;
            if (entry.State == EntityState.Added)
            {
                if (name == "Id") continue;
                newValues[name] = Mask(name, p.CurrentValue);
            }
            else if (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
            {
                oldValues ??= [];
                oldValues[name] = Mask(name, p.OriginalValue);
                newValues[name] = Mask(name, p.CurrentValue);
            }
        }
        if (entry.State != EntityState.Added && newValues.Count == 0) return;
        if (!string.IsNullOrWhiteSpace(reason)) newValues["_reason"] = reason;
        state.Pending.Add((entry, action, oldValues, newValues));
    }

    private static object? Mask(string name, object? value) =>
        MaskedColumns.Contains(name) && value is not null ? "***" : value;

    private bool AddAuditRows(DbContext ctx, State state)
    {
        if (state.Pending.Count == 0) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var (entry, action, oldValues, newValues) in state.Pending)
        {
            var entity = entry.Entity;
            long? tenantId = entity switch
            {
                ITenantScoped t => t.TenantId,
                INullableTenantScoped n => n.TenantId,
                Tenant tn => tn.Id,
                _ => scope.TenantId,
            };
            long? companyId = entity switch
            {
                ICompanyScoped c => c.CompanyId,
                Company co => co.Id,
                _ => entry.Properties.FirstOrDefault(p => p.Metadata.Name == "CompanyId")?.CurrentValue as long?,
            };
            ctx.Set<AuditLog>().Add(new AuditLog
            {
                TenantId = tenantId,
                CompanyId = companyId,
                UserId = scope.UserId,
                OccurredAt = now,
                Action = action,
                EntityType = EntityTypeName(entity),
                EntityId = entry.Property("Id").CurrentValue as long?,
                OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues, Json),
                NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues, Json),
                IpAddress = scope.IpAddress,
            });
        }
        state.Pending.Clear();
        return true;
    }

    /// <summary>Namespaced entity type written to the audit log, e.g. TRN.Trip, CORE.User (design §3.2).</summary>
    public static string EntityTypeName(object entity)
    {
        var type = entity.GetType();
        var module = type.Namespace?.Split('.').LastOrDefault()?.ToUpperInvariant() ?? "APP";
        return $"{module}.{type.Name}";
    }
}
