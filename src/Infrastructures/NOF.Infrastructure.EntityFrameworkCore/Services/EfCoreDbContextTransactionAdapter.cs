using Microsoft.EntityFrameworkCore;
using NOF.Application;
using System.Data;
using EfDbContextTransaction = Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Adapts the native transaction to the application's minimal transaction boundary without wrapping failures.</summary>
public sealed class EfCoreDbContextTransactionAdapter(EfDbContextTransaction transaction) : ITransaction
{
    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken cancellationToken = default) => transaction.RollbackAsync(cancellationToken);
    /// <inheritdoc />
    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
