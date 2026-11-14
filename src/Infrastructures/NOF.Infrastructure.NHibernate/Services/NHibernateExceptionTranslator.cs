using NHibernate;
using AppDbException = NOF.Application.DbException;
using AppDbTransactionCommitException = NOF.Application.DbTransactionCommitException;
using AppDbTransactionException = NOF.Application.DbTransactionException;
using AppDbUpdateConcurrencyException = NOF.Application.DbUpdateConcurrencyException;
using AppDbUpdateException = NOF.Application.DbUpdateException;

namespace NOF.Infrastructure.NHibernate;
/// <summary>Translates persistence exceptions for NHibernate.</summary>

public static class NHibernateExceptionTranslator
{
    public static Exception TranslateSaveChangesException(Exception ex)
        => ex switch
        {
            StaleStateException => new AppDbUpdateConcurrencyException(ex.Message, ex),
            ADOException => new AppDbUpdateException(ex.Message, ex),
            HibernateException => new AppDbException(ex.Message, ex),
            _ => ex
        };

    public static Exception TranslateTransactionException(Exception ex, string message)
        => ex is HibernateException
            ? new AppDbTransactionException(message, ex)
            : ex;

    public static Exception TranslateCommitException(Exception ex)
        => ex is HibernateException
            ? new AppDbTransactionCommitException(ex.Message, ex)
            : ex;
}
