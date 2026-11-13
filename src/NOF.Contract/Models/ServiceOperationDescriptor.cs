using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Contract;

/// <summary>Identifies one contract operation independently of its implementation and transport.</summary>
public abstract class ServiceOperationDescriptor
{
    /// <summary>Creates contract operation metadata.</summary>
    protected ServiceOperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string operationName, bool allowsNullResponse)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ServiceType = serviceType;
        OperationName = operationName;
        AllowsNullResponse = allowsNullResponse;
        MethodInfo = serviceType.GetMethod(operationName)
            ?? throw new ArgumentException("The contract operation does not exist.", nameof(operationName));
    }

    /// <summary>Gets the contract type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public Type ServiceType { get; }
    /// <summary>Gets the operation name in its contract.</summary>
    public string OperationName { get; }
    /// <summary>Gets an identity unaffected by handler renames.</summary>
    public string Id => $"{ServiceType.FullName}/{OperationName}";
    /// <summary>Gets the declared contract method and policy metadata.</summary>
    public MethodInfo MethodInfo { get; }
    /// <summary>Gets the declared input type.</summary>
    public abstract Type RequestType { get; }
    /// <summary>Gets the declared output type.</summary>
    public abstract Type ResponseType { get; }
    /// <summary>Gets whether the contract allows a null output.</summary>
    public bool AllowsNullResponse { get; }
}

/// <summary>A typed contract operation reference shared by producers and consumers.</summary>
public sealed class ServiceOperationDescriptor<TRequest, TResponse> : ServiceOperationDescriptor
{
    /// <summary>Creates a typed operation reference.</summary>
    public ServiceOperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string operationName, bool allowsNullResponse = false) : base(serviceType, operationName, allowsNullResponse)
    {
        if (MethodInfo.GetParameters() is not [{ ParameterType: var input }] || input != typeof(TRequest))
        {
            throw new ArgumentException("The descriptor input must match its contract.", nameof(serviceType));
        }
    }
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);
    /// <inheritdoc />
    public override Type ResponseType => typeof(TResponse);
}
