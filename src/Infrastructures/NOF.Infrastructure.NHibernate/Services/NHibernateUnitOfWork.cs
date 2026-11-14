using NOF.Application;
using System.Data;

namespace NOF.Infrastructure.NHibernate;

/// <summary>Shares the native NHibernate session across business ports and framework stores.</summary>
public sealed class NHibernateUnitOfWork(global::NHibernate.ISession session) : IUnitOfWork, ITransactionManager
{
    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await session.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (global::NHibernate.StaleStateException error)
        {
            throw new DBConcurrencyException(error.Message, error);
        }
        catch (global::NHibernate.ADOException error)
        {
            throw new DataException(error.Message, error);
        }
    }
    /// <inheritdoc />
    public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ITransaction>(new NHibernateApplicationTransaction(session.BeginTransaction()));
    }
}

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
