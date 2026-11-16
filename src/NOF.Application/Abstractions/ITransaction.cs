namespace NOF.Application;

/// <summary>Controls an explicit application transaction; disposal releases uncommitted work.</summary>
public interface ITransaction : IAsyncDisposable
{
    /// <summary>Commits flushed business changes and staged integration messages; native errors and cancellation propagate.</summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>Rolls back database writes; callers discard the scope before retrying.</summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
