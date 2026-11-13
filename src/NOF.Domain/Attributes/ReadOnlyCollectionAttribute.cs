namespace NOF.Domain;

/// <summary>Generates a cached live read-only view over a private readonly List or HashSet field.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ReadOnlyCollectionAttribute(string? name = null) : Attribute
{
    /// <summary>Gets an optional public property name; otherwise _items becomes Items.</summary>
    public string? Name { get; } = name;
}

/// <summary>Associates a generated collection property with its aggregate-owned mutable field.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CollectionBackingFieldAttribute(string name) : Attribute
{
    /// <summary>Gets the exact aggregate field name.</summary>
    public string Name { get; } = name;
}
