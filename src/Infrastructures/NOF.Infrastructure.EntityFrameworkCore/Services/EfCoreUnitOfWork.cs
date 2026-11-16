using Microsoft.EntityFrameworkCore;
using NOF.Application;
using System.Data;
using EfDbContextTransaction = Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Shares the native scoped DbContext across business ports and framework stores.</summary>
public sealed class EfCoreUnitOfWork(DbContext context) : IUnitOfWork, ITransactionManager
{
    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException error)
        {
            throw new DBConcurrencyException(error.Message, error);
        }
        catch (DbUpdateException error)
        {
            throw new DataException(error.Message, error);
        }
    }

    /// <inheritdoc />
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => new EfCoreDbContextTransactionAdapter(await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));
}
