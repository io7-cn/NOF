using NOF.Application;
using System.Data;

namespace NOF.Infrastructure.NHibernate;

/// <summary>Projects a native transaction onto the application transaction boundary without wrapping failures.</summary>
public sealed class NHibernateApplicationTransaction(global::NHibernate.ITransaction transaction) : ITransaction
{
    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken = default) => transaction.RollbackAsync(cancellationToken);
    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        transaction.Dispose();
        return ValueTask.CompletedTask;
    }
}
