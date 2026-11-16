namespace NOF.Domain;

/// <summary>Associates a generated collection property with its aggregate-owned mutable field.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CollectionBackingFieldAttribute(string name) : Attribute
{
    /// <summary>Gets the exact aggregate field name.</summary>
    public string Name { get; } = name;
}
