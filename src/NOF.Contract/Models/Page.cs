namespace NOF.Contract;

/// <summary>A page of query data; operations may include it as a successful union case.</summary>
/// <typeparam name="T">The item type.</typeparam>
public sealed record Page<T>
{
    /// <summary>Creates a page with an optional nonnegative total count.</summary>
    public Page(T[] items, int? totalCount = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount));
        }

        Items = items;
        TotalCount = totalCount;
    }

    /// <summary>Gets the page items.</summary>
    public T[] Items { get; }

    /// <summary>Gets the total count, when known.</summary>
    public int? TotalCount { get; }
}
