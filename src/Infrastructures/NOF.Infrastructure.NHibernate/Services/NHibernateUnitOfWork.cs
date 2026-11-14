using NOF.Application;

namespace NOF.Infrastructure.NHibernate;

/// <summary>Shares the native NHibernate session across business ports and framework stores.</summary>
public sealed class NHibernateUnitOfWork(global::NHibernate.ISession session) : IUnitOfWork, ITransactionManager
{
    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await session.FlushAsync(cancellationToken);
        }
        catch (Exception error)
        {
            throw NHibernateExceptionTranslator.TranslateSaveChangesException(error);
        }
    }
    /// <inheritdoc />
    public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ITransaction>(new NHibernateApplicationTransaction(session.BeginTransaction()));
    }
}
/// <summary>Projects a native transaction onto the application transaction boundary.</summary>
public sealed class NHibernateApplicationTransaction(global::NHibernate.ITransaction transaction) : ITransaction
{
    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception error)
        {
            throw NHibernateExceptionTranslator.TranslateCommitException(error);
        }
    }
    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception error)
        {
            throw NHibernateExceptionTranslator.TranslateTransactionException(error, "Failed to roll back the database transaction.");
        }
    }
    /// <inheritdoc />
    public ValueTask DisposeAsync() { transaction.Dispose(); return ValueTask.CompletedTask; }
}
