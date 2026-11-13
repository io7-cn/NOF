using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Contract;

/// <summary>Identifies a declared operation without promising an application-generated response type.</summary>
public abstract class ServiceOperationReference
{
    /// <summary>Resolves the contract identity and declaration metadata.</summary>
    protected ServiceOperationReference(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType, string operationName)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ServiceType = serviceType;
        OperationName = operationName;
        MethodInfo = serviceType.GetMethod(operationName)
            ?? throw new ArgumentException("The contract operation does not exist.", nameof(operationName));
    }
    /// <summary>Gets the declaring contract type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public Type ServiceType { get; }
    /// <summary>Gets the declared operation name.</summary>
    public string OperationName { get; }
    /// <summary>Gets the stable contract and operation identity.</summary>
    public string Id => $"{ServiceType.FullName}/{OperationName}";
    /// <summary>Gets the declared method and its failure/policy metadata.</summary>
    public MethodInfo MethodInfo { get; }
    /// <summary>Gets the declared input type.</summary>
    public abstract Type RequestType { get; }
    /// <summary>Gets whether submission may discard no business output or declared failure.</summary>
    public bool CanSubmit => MethodInfo.ReturnType == typeof(Empty) && !MethodInfo.IsDefined(typeof(FailureCaseAttribute), inherit: false);
}

/// <summary>A Contract-owned typed input reference usable without an Application assembly.</summary>
public sealed class ServiceOperationReference<TRequest> : ServiceOperationReference
{
    /// <summary>Creates an operation reference and validates its declared input.</summary>
    public ServiceOperationReference(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType, string operationName) : base(serviceType, operationName)
    {
        if (MethodInfo.GetParameters() is not [{ ParameterType: var input }] || input != typeof(TRequest))
        {
            throw new ArgumentException("The operation input must match its contract.", nameof(serviceType));
        }
    }
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);
}
