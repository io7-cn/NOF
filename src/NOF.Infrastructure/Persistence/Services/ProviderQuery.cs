using NOF.Domain;
using NOF.Infrastructure;
using System.Collections;
using System.ComponentModel;
using System.Linq.Expressions;

namespace NOF.Infrastructure;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface IProviderQueryAccessor
{
    IQueryable Query { get; }
}

/// <summary>
/// Default wrapper that preserves asynchronous query execution metadata across LINQ composition.
/// </summary>
public class ProviderQuery<TSource> : IProviderQuery<TSource>, IOrderedQueryable<TSource>, IProviderQueryAccessor
{
    private readonly IQueryable<TSource> _query;
    private readonly ProviderQueryProvider _provider;

    public ProviderQuery(IQueryable<TSource> query, IProviderQueryExecutor asyncExecutor)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(asyncExecutor);

        _query = query;
        AsyncExecutor = asyncExecutor;
        _provider = new ProviderQueryProvider(query.Provider, asyncExecutor);
    }

    public IProviderQueryExecutor AsyncExecutor { get; }

    public Type ElementType => _query.ElementType;

    public Expression Expression => _query.Expression;

    public IQueryProvider Provider => _provider;

    IQueryable IProviderQueryAccessor.Query => _query;

    public IEnumerator<TSource> GetEnumerator()
        => _query.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();
}
