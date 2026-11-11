using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Hosting.AspNetCore;
using NOF.Internal;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Microsoft.AspNetCore.Routing;
/// <summary>Maps NOF RPC operations to ASP.NET Core endpoints.</summary>

public static partial class NOFHostingAspNetCoreExtensions
{
    [RequiresUnreferencedCode("JSON-RPC endpoint mapping uses runtime request and response type metadata.")]
    [RequiresDynamicCode("JSON-RPC endpoint mapping uses runtime request and response type metadata.")]
    internal static IEndpointRouteBuilder MapJsonRpcEndpoint(
        IEndpointRouteBuilder app,
        Type rpcServerType)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(rpcServerType);

        if (!typeof(ApplicationService).IsAssignableFrom(rpcServerType)
            || !typeof(IApplicationServiceImplementation).IsAssignableFrom(rpcServerType))
        {
            throw new InvalidOperationException($"Type '{rpcServerType.FullName}' is not a valid RPC server type.");
        }

        var serviceType = rpcServerType.GetProperty(
                nameof(IApplicationServiceType.ServiceType),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as Type
            ?? throw new InvalidOperationException($"RPC server type '{rpcServerType.FullName}' does not expose static property '{nameof(IApplicationServiceType.ServiceType)}'.");

        var handlerMappings = rpcServerType.GetProperty(
                nameof(IApplicationServiceImplementation.HandlerMappings),
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            ?.GetValue(null) as IReadOnlyDictionary<string, ServiceOperationMapping>
            ?? throw new InvalidOperationException($"RPC server type '{rpcServerType.FullName}' does not expose static property '{nameof(IApplicationServiceImplementation.HandlerMappings)}'.");

        return MapJsonRpcEndpointCore(app, rpcServerType, serviceType, handlerMappings);
    }

    private static IEndpointRouteBuilder MapJsonRpcEndpointCore(
        IEndpointRouteBuilder app,
        Type rpcServerType,
        Type serviceType,
        IReadOnlyDictionary<string, ServiceOperationMapping> handlerMappings)
    {
        var resolvedPattern = HttpRoutePrefix.Normalize(GetHttpRoutePrefix(serviceType) ?? "/");

        var mappingState = app.ServiceProvider.GetService<HttpEndpointMappingState>();
        var mappingKey = $"{rpcServerType.AssemblyQualifiedName}|JsonRpc";
        if (mappingState is not null && !mappingState.TryMarkMapped(mappingKey))
        {
            return app;
        }

        var handler = JsonRpcEndpointHandler.Create(serviceType, handlerMappings);
        app.MapPost(resolvedPattern, handler);
        return app;
    }
}
