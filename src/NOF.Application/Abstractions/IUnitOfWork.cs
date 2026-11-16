namespace NOF.Application;

/// <summary>Saves the current use case's staged business changes and integration messages together.</summary>
public interface IUnitOfWork
{
    /// <summary>Flushes changes; an explicit surrounding transaction is committed separately.</summary>
    /// <exception cref="System.Data.DBConcurrencyException">A native optimistic-lock check failed; discard the scope before retrying.</exception>
    /// <exception cref="System.Data.DataException">The provider could not save data; the native cause is retained as InnerException.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
