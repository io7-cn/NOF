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
