namespace NOF.Abstraction;

/// <summary>Describes a JSON converter that delegates its wire representation to a primitive type.</summary>
public interface IJsonPrimitiveConverter
{
    /// <summary>Gets the primitive type whose JSON metadata defines the wire representation.</summary>
    Type PrimitiveType { get; }
}
