using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Application;

/// <summary>Identifies one contract operation independently of its implementation and transport.</summary>
public abstract class ServiceOperationDescriptor : ServiceOperationReference
{
    /// <summary>Creates execution metadata with a complete Application-owned output type.</summary>
    protected ServiceOperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string operationName, bool allowsNullResponse) : base(serviceType, operationName)
    {
        AllowsNullResponse = allowsNullResponse;
        MethodInfo = ServiceOperationInspector.ResolveMethod(this);
    }
    /// <summary>Gets resolved Contract method and policy metadata.</summary>
    public System.Reflection.MethodInfo MethodInfo { get; }
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
    /// <summary>Creates a typed operation reference.</summary>
    public ServiceOperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string operationName, bool allowsNullResponse = false, IReadOnlyList<ServiceOperationResultCase>? resultCases = null) : base(serviceType, operationName, allowsNullResponse)
    {
        Reference = new(serviceType, operationName);
        ServiceOperationInspector.ValidateReference(Reference);
        ResultCases = Array.AsReadOnly((resultCases ?? [new(typeof(TResponse))]).ToArray());
    }
    /// <summary>Gets the input-only reference for producer routing.</summary>
    public ServiceOperationReference<TRequest> Reference { get; }
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);
    /// <inheritdoc />
    public override Type ResponseType => typeof(TResponse);
    /// <inheritdoc />
    public override IReadOnlyList<ServiceOperationResultCase> ResultCases { get; }
}
