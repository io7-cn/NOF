using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

/// <summary>Identifies a declared operation without promising an application-generated response type.</summary>
public abstract class ServiceOperationReference
{
    /// <summary>Creates a declarative contract operation identity.</summary>
    protected ServiceOperationReference(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType, string operationName)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ServiceType = serviceType;
        OperationName = operationName;
    }
    /// <summary>Gets the declaring contract type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public Type ServiceType { get; }
    /// <summary>Gets the declared operation name.</summary>
    public string OperationName { get; }
    /// <summary>Gets the stable contract and operation identity.</summary>
    public string Id => $"{ServiceType.FullName}/{OperationName}";
    /// <summary>Gets the declared input type.</summary>
    public abstract Type RequestType { get; }
}

/// <summary>A typed producer reference usable without the receiving application implementation.</summary>
public sealed class ServiceOperationReference<TRequest> : ServiceOperationReference
{
    /// <summary>Creates an operation identity with its declared input type.</summary>
    public ServiceOperationReference(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType, string operationName) : base(serviceType, operationName)
    {
    }
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);
}
