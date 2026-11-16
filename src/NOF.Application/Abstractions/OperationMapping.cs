using NOF.Contract;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

/// <summary>
/// One application operation mapping entry.
/// </summary>
public sealed record OperationMapping(
    [param: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicMethods
        | DynamicallyAccessedMemberTypes.NonPublicMethods)]
    [property: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicMethods
        | DynamicallyAccessedMemberTypes.NonPublicMethods)]
    Type HandlerType,
    [param: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicProperties)]
    [property: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicConstructors
        | DynamicallyAccessedMemberTypes.PublicProperties)]
    Type RequestType,
    [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    Type ReturnType)
{
    /// <summary>Gets the complete generated descriptor for construction-free plan validation.</summary>
    public OperationDescriptor? Descriptor { get; init; }
}
