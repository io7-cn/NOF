using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Hosting;

namespace NOF.Hosting.AspNetCore;

/// <summary>Maps registered endpoint modules once during host initialization.</summary>
public sealed class HttpEndpointModuleInitializationStep : IApplicationInitializationStep
{
    /// <inheritdoc />
    public TopologyComparison Compare(IApplicationInitializationStep other)
        => other is DaemonServiceResolutionInitializationStep ? TopologyComparison.After : TopologyComparison.DoesNotMatter;

    /// <inheritdoc />
    public Task ExecuteAsync(IHost host)
    {
        if (host is IEndpointRouteBuilder endpoints)
        {
            foreach (var module in host.Services.GetServices<IHttpEndpointModule>())
            {
                module.Map(endpoints);
            }
        }
        return Task.CompletedTask;
    }
}
