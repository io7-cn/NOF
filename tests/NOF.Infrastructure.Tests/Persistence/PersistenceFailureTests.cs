using Microsoft.EntityFrameworkCore;
using Moq;
using NOF.Application;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Infrastructure.NHibernate;
using System.Data;
using Xunit;

namespace NOF.Infrastructure.Tests.Persistence;

public sealed class PersistenceFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_UsesStandardPersistenceErrors_AndPreservesNativeCauses(bool nhibernate)
    {
        Exception concurrency = nhibernate ? new global::NHibernate.StaleStateException("stale")
            : new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("stale");
        Exception data = nhibernate ? new global::NHibernate.ADOException("database", new InvalidOperationException("driver"))
            : new Microsoft.EntityFrameworkCore.DbUpdateException("database");
        var conflict = await Assert.ThrowsAsync<DBConcurrencyException>(() => Work(concurrency, nhibernate).SaveChangesAsync());
        Assert.Same(concurrency, conflict.InnerException);
        var failed = await Assert.ThrowsAsync<DataException>(() => Work(data, nhibernate).SaveChangesAsync());
        Assert.Same(data, failed.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_DoesNotWrapCancellationOrUnexpectedErrors(bool nhibernate)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var error in new Exception[] { new OperationCanceledException(cancellation.Token), new InvalidOperationException("hook") })
        {
            var caught = await Record.ExceptionAsync(() => Work(error, nhibernate).SaveChangesAsync(cancellation.Token));
            Assert.Same(error, caught);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Transactions_PropagateNativeErrorsAndCancellation(bool nhibernate)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var error in new Exception[] { new InvalidOperationException("commit"), new OperationCanceledException(cancellation.Token) })
        {
            ITransaction adapter;
            if (nhibernate)
            {
                var native = new Mock<global::NHibernate.ITransaction>();
                native.Setup(value => value.CommitAsync(cancellation.Token)).ThrowsAsync(error);
                native.Setup(value => value.RollbackAsync(cancellation.Token)).ThrowsAsync(error);
                adapter = new NHibernateApplicationTransaction(native.Object);
            }
            else
            {
                var native = new Mock<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>();
                native.Setup(value => value.CommitAsync(cancellation.Token)).ThrowsAsync(error);
                native.Setup(value => value.RollbackAsync(cancellation.Token)).ThrowsAsync(error);
                adapter = new EfCoreDbContextTransactionAdapter(native.Object);
            }
            await using (adapter)
            {
                Assert.Same(error, await Record.ExceptionAsync(() => adapter.CommitAsync(cancellation.Token)));
                Assert.Same(error, await Record.ExceptionAsync(() => adapter.RollbackAsync(cancellation.Token)));
            }
        }
    }

    private static IUnitOfWork Work(Exception error, bool nhibernate)
    {
        if (nhibernate)
        {
            var native = new Mock<global::NHibernate.ISession>();
            native.Setup(value => value.FlushAsync(It.IsAny<CancellationToken>())).ThrowsAsync(error);
            return new NHibernateUnitOfWork(native.Object);
        }
        var db = new Mock<DbContext>();
        db.Setup(value => value.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(error);
        return new EfCoreUnitOfWork(db.Object);
    }
}
