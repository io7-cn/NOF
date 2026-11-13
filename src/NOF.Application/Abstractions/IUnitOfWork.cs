namespace NOF.Application;

/// <summary>Saves the current use case's staged business changes and integration messages together.</summary>
public interface IUnitOfWork
{
    /// <summary>Flushes changes; an explicit surrounding transaction is committed separately.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Begins an explicit transaction shared by repositories and message staging in the current scope.</summary>
public interface ITransactionManager
{
    /// <summary>Begins the current scope's transaction.</summary>
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>Controls an explicit application transaction; disposal releases uncommitted work.</summary>
public interface ITransaction : IAsyncDisposable
{
    /// <summary>Commits flushed business changes and staged integration messages.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>Rolls back database writes; callers discard the scope before retrying.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
