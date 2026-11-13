using System.Collections;

namespace NOF.Domain;

/// <summary>A live view exposing no mutable collection interface and never copying its elements.</summary>
public sealed class ReadOnlyCollectionView<T>(ICollection<T> collection) : IReadOnlyCollection<T>
{
    private readonly ICollection<T> _collection = collection ?? throw new ArgumentNullException(nameof(collection));
    /// <inheritdoc />
    public int Count => _collection.Count;
    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _collection.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
