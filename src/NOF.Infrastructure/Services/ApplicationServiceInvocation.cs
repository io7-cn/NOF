using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>Provides the scoped server-side invocation boundary used by generated operations.</summary>
public sealed class ApplicationServiceInvocation(IServiceProvider services) : IApplicationServiceInvoker
{
    /// <inheritdoc />
    public Task<ServiceExecution> InvokeAsync<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TService>(
        string operationName, object request, Context context, CancellationToken cancellationToken = default)
        where TService : class, IApplicationService
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        var method = InboundContextReflection.ResolveServiceMethodInfo(typeof(TService), operationName);
        return ApplicationServiceInvoker.InvokeAsync<TService>(services, method, request, context, cancellationToken);
    }
}
