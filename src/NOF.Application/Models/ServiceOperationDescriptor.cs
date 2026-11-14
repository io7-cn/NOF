using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Application;

/// <summary>Identifies one contract operation independently of its implementation and transport.</summary>
public abstract class ServiceOperationDescriptor
{
    /// <summary>Creates execution metadata with a complete Application-owned output type.</summary>
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
            ?? throw new ArgumentException($"Operation '{Id}' does not exist.", nameof(operationName));
    }
    /// <summary>Gets the declaring contract.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public Type ServiceType { get; }
    /// <summary>Gets the declared operation name.</summary>
    public string OperationName { get; }
    /// <summary>Gets the contract-qualified identity.</summary>
    public string Id => $"{ServiceType.FullName}/{OperationName}";
    /// <summary>Gets the business input type.</summary>
    public abstract Type RequestType { get; }
    /// <summary>Gets resolved Contract method and policy metadata.</summary>
    public MethodInfo MethodInfo { get; }
    /// <summary>Gets the complete business output type, including generated failure branches.</summary>
    public abstract Type ResponseType { get; }
    /// <summary>Gets whether execution allows a null output.</summary>
    public bool AllowsNullResponse { get; }
    /// <summary>Gets statically described business branches.</summary>
    public virtual IReadOnlyList<ServiceOperationResultCase> ResultCases => [new(ResponseType)];
}

/// <summary>A typed execution descriptor whose output is known by the Application implementation.</summary>
public sealed class ServiceOperationDescriptor<TRequest, TResponse> : ServiceOperationDescriptor
{
    /// <summary>Creates a typed execution descriptor and validates the declared input.</summary>
    public ServiceOperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string operationName, bool allowsNullResponse = false, IReadOnlyList<ServiceOperationResultCase>? resultCases = null) : base(serviceType, operationName, allowsNullResponse)
    {
        if (MethodInfo.GetParameters() is not [{ ParameterType: var input }] || input != typeof(TRequest))
        {
            throw new ArgumentException("The operation input must match its contract.", nameof(serviceType));
        }
        ResultCases = Array.AsReadOnly((resultCases ?? [new(typeof(TResponse))]).ToArray());
    }
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);
    /// <inheritdoc />
    public override Type ResponseType => typeof(TResponse);
    /// <inheritdoc />
    public override IReadOnlyList<ServiceOperationResultCase> ResultCases { get; }
}
