using System.Runtime.CompilerServices;

namespace DirectService.Transportation.Application.Abstractions;

/// <summary>Default mutable, request-scoped implementation of <see cref="IDataScope"/>.</summary>
public sealed class DataScope : IDataScope
{
    public long? TenantId { get; private set; }
    public long? CompanyId { get; private set; }
    public long? UserId { get; private set; }
    public string? IpAddress { get; private set; }

    /// <summary>Sets the scope for the current request (called once by the request pipeline).</summary>
    public void Set(long? tenantId, long? companyId, long? userId, string? ipAddress = null)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        UserId = userId;
        IpAddress = ipAddress;
    }

    public IDisposable Use(long? tenantId, long? companyId, long? userId)
    {
        var previous = (TenantId, CompanyId, UserId);
        TenantId = tenantId;
        CompanyId = companyId;
        UserId = userId;
        return new Restore(this, previous);
    }

    private sealed class Restore(DataScope scope, (long? T, long? C, long? U) previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            scope.TenantId = previous.T;
            scope.CompanyId = previous.C;
            scope.UserId = previous.U;
        }
    }
}

/// <summary>Default implementation of <see cref="IChangeContext"/>.</summary>
public sealed class ChangeContext : IChangeContext
{
    private readonly ConditionalWeakTable<object, Tuple<string, string?>> _actions = [];

    public void SetAction(object entity, string action, string? reason = null) => _actions.AddOrUpdate(entity, Tuple.Create(action, reason));

    public string? GetAction(object entity) => _actions.TryGetValue(entity, out var a) ? a.Item1 : null;

    public string? GetReason(object entity) => _actions.TryGetValue(entity, out var a) ? a.Item2 : null;

    public void Reset() => _actions.Clear();
}
