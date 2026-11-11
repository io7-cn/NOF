using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

public sealed record ApplicationServiceInvocationResolution(
    Type ServerType,
    ServiceOperationMapping HandlerMapping);

public sealed class ApplicationServiceInvocationResolver(
    IServiceProvider serviceProvider,
    ApplicationServiceRegistry rpcServerRegistry)
{
    public ApplicationServiceInvocationResolution Resolve<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TService>(
        string operationName)
        where TService : class, IApplicationService
        => Resolve(typeof(TService), operationName);

    public ApplicationServiceInvocationResolution Resolve(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type serviceType,
        string operationName)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (!rpcServerRegistry.TryGetImplementationType(serviceType, out var serverType))
        {
            throw new InvalidOperationException($"RPC server is not registered for '{serviceType.FullName}'.");
        }

        var server = serviceProvider.GetRequiredService(serverType) as ApplicationService
            ?? throw new InvalidOperationException($"Resolved RPC server '{serverType.FullName}' does not inherit ApplicationService.");

        if (!server.TryGetHandlerMapping(operationName, out var handlerMapping))
        {
            throw new InvalidOperationException($"RPC handler mapping is missing for '{serviceType.FullName}.{operationName}'.");
        }

        return new ApplicationServiceInvocationResolution(serverType, handlerMapping);
    }
}
