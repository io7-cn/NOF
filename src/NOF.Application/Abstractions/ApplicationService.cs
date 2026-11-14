using NOF.Contract;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface IApplicationServiceType
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    static abstract Type ServiceType { get; }
}

public interface IApplicationServiceImplementation : IApplicationServiceType
{
    static abstract IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; }
}

/// <summary>
/// One application operation mapping entry.
/// </summary>
public sealed record ServiceOperationMapping(
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
    public ServiceOperationDescriptor? Descriptor { get; init; }
}

/// <summary>
/// Base type for source-generated application service containers.
/// </summary>
public abstract class ApplicationService;

/// <summary>
/// Typed base class for an application service container.
/// </summary>
public abstract class ApplicationService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] TService> : ApplicationService, IApplicationServiceType
    where TService : class, IApplicationService
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    public static Type ServiceType => typeof(TService);
}
