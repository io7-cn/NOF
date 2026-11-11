using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

/// <summary>Dispatches a declared server operation through the application invocation pipelines.</summary>
/// <remarks>Generated operation entry points use this boundary without creating client interfaces or proxies.</remarks>
public interface IApplicationServiceInvoker
{
    /// <summary>Invokes an operation identified by its proto-style contract and operation name.</summary>
    /// <typeparam name="TService">The declarative application service contract.</typeparam>
    Task<ServiceExecution> InvokeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TService>(
        string operationName, object request, Context context, CancellationToken cancellationToken = default)
        where TService : class, IApplicationService;
}
