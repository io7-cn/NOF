using NOF.Infrastructure;
namespace NOF.Infrastructure;

/// <summary>
/// Represents a queryable source whose asynchronous terminal operations are provided by an adapter-specific executor.
/// </summary>
public interface IProviderQuery<out TSource> : IQueryable<TSource>
{
    IProviderQueryExecutor AsyncExecutor { get; }
}
