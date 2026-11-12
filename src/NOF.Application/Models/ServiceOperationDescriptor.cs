using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

/// <summary>Identifies a generated application operation without selecting a transport.</summary>
/// <typeparam name="TRequest">The contract request.</typeparam>
/// <typeparam name="TResponse">The declared business response.</typeparam>
public sealed class ServiceOperationDescriptor<TRequest, TResponse>
{
    /// <summary>Creates an operation reference from its declaration.</summary>
    public ServiceOperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string operationName, bool allowsNullResponse = false)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        ServiceType = serviceType;
        OperationName = operationName;
        AllowsNullResponse = allowsNullResponse;
    }

    /// <summary>Gets the declaring service contract.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public Type ServiceType { get; }

    /// <summary>Gets the operation name within the service.</summary>
    public string OperationName { get; }

    /// <summary>Gets whether a null response is declared.</summary>
    public bool AllowsNullResponse { get; }
}
