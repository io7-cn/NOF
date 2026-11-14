using Microsoft.EntityFrameworkCore;
using NOF.Application;
using NOF.Domain;
using NOF.Infrastructure;
using System.Linq.Expressions;
using System.Reflection;
using AppDbException = NOF.Application.DbException;
using AppDbTransactionCommitException = NOF.Application.DbTransactionCommitException;
using AppDbTransactionException = NOF.Application.DbTransactionException;
using AppDbUpdateConcurrencyException = NOF.Application.DbUpdateConcurrencyException;
using AppDbUpdateException = NOF.Application.DbUpdateException;
using EfDbContextTransaction = Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction;
using EfDbUpdateConcurrencyException = Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException;
using EfDbUpdateException = Microsoft.EntityFrameworkCore.DbUpdateException;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Shares the native scoped DbContext across business ports and framework stores.</summary>
public sealed class EfCoreUnitOfWork(DbContext context) : IUnitOfWork, ITransactionManager
{
    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception error)
        {
            throw EfCoreExceptionTranslator.TranslateSaveChangesException(error);
        }
    }
    /// <inheritdoc />
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => new EfCoreDbContextTransactionAdapter(await context.Database.BeginTransactionAsync(cancellationToken));
}

/// <summary>Adapts the native transaction to the application's minimal transaction boundary.</summary>
public sealed class EfCoreDbContextTransactionAdapter(EfDbContextTransaction transaction) : ITransaction
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
            throw EfCoreExceptionTranslator.TranslateCommitException(error);
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
            throw EfCoreExceptionTranslator.TranslateTransactionException(error, "Failed to roll back the database transaction.");
        }
    }
    /// <inheritdoc />
    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}

/// <summary>Translates native EF Core persistence failures at the unit-of-work boundary.</summary>
public static class EfCoreExceptionTranslator
{
    /// <summary>Translates a native save failure into the application persistence exception.</summary>
    public static Exception TranslateSaveChangesException(Exception exception)
        => exception switch
        {
            AppDbException => exception,
            EfDbUpdateConcurrencyException ex => new AppDbUpdateConcurrencyException(
                "A concurrency violation was detected while saving changes.",
                ex),
            EfDbUpdateException ex => new AppDbUpdateException(
                "An error occurred while saving changes to the database.",
                ex),
            _ => exception
        };

    /// <summary>Translates a native transaction failure while retaining its cause.</summary>
    public static AppDbTransactionException TranslateTransactionException(Exception exception, string message)
        => exception switch
        {
            AppDbTransactionException ex => ex,
            _ => new AppDbTransactionException(message, exception)
        };

    /// <summary>Translates a native transaction commit failure while retaining its cause.</summary>
    public static AppDbTransactionCommitException TranslateCommitException(Exception exception)
        => exception switch
        {
            AppDbTransactionCommitException ex => ex,
            _ => new AppDbTransactionCommitException(
                "Failed to commit the database transaction.",
                exception)
        };
}
