using NOF.Domain;
using NOF.Infrastructure;
using System.Linq.Expressions;

namespace NOF.Infrastructure;

public sealed class ProviderQueryProvider(IQueryProvider innerProvider, IProviderQueryExecutor asyncExecutor) : IQueryProvider
{
    private readonly IQueryProvider _innerProvider = innerProvider;
    private readonly IProviderQueryExecutor _asyncExecutor = asyncExecutor;

    public IQueryable CreateQuery(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return _innerProvider.CreateQuery(expression);
    }

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return new ProviderQuery<TElement>(_innerProvider.CreateQuery<TElement>(expression), _asyncExecutor);
    }

    public object? Execute(Expression expression)
        => _innerProvider.Execute(expression);

    public TResult Execute<TResult>(Expression expression)
        => _innerProvider.Execute<TResult>(expression);
}
