namespace NOF.Infrastructure;

/// <summary>Provider set used only inside persistence adapters, without ICollection or detached-update operations.</summary>
public interface IEntitySet<TEntity> : IProviderQuery<TEntity> where TEntity : class
{
    /// <summary>Add within the current provider scope; callers retain the shared unit-of-work boundary.</summary>
    void Add(TEntity entity);
    /// <summary>Add within the current provider scope; callers retain the shared unit-of-work boundary.</summary>
    ValueTask AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    /// <summary>Remove within the current provider scope; callers retain the shared unit-of-work boundary.</summary>
    bool Remove(TEntity entity);
    /// <summary>AsNoTracking within the current provider scope; callers retain the shared unit-of-work boundary.</summary>
    IProviderQuery<TEntity> AsNoTracking();
}
