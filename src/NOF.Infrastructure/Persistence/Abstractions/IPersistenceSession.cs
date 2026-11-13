namespace NOF.Infrastructure;

/// <summary>Low-level scoped provider collaboration for infrastructure stores; application code uses IUnitOfWork and owned ports.</summary>
public interface IPersistenceSession
{
    /// <summary>Set within the current provider scope; callers retain the shared unit-of-work boundary.</summary>
    IEntitySet<TEntity> Set<TEntity>() where TEntity : class;
    /// <summary>SaveChanges within the current provider scope; callers retain the shared unit-of-work boundary.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    /// <summary>BeginTransaction within the current provider scope; callers retain the shared unit-of-work boundary.</summary>
    Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
