using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DirectService.SharedKernel;

/// <summary>The Master Database as seen by the application layer (implemented by MasterDbContext).</summary>
public interface IAppDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    Microsoft.EntityFrameworkCore.Metadata.IModel Model { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
