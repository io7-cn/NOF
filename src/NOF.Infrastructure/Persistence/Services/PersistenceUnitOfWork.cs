using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Projects the scoped provider session onto the application's narrow commit boundary.</summary>
public sealed class PersistenceUnitOfWork(IPersistenceSession session) : IUnitOfWork, ITransactionManager
{
    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _ = await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    /// <inheritdoc />
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => await session.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
}
