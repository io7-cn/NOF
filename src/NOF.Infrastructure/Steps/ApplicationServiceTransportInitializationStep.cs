using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Application;
using NOF.Hosting;

namespace NOF.Infrastructure;
/// <summary>Initializes registered RPC server transports.</summary>

public sealed class ApplicationServiceTransportInitializationStep : IApplicationInitializationStep
{
    public TopologyComparison Compare(IApplicationInitializationStep other)
        => TopologyComparison.DoesNotMatter;

    public async Task ExecuteAsync(IHost app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var registry = app.Services.GetRequiredService<ApplicationServiceRegistry>();
        var registrations = registry.Freeze();
        var transports = new DependencyGraph<IApplicationServiceTransport>(
            app.Services.GetServices<IApplicationServiceTransport>()).GetExecutionOrder();

        foreach (var transport in transports)
        {
            foreach (var registration in registrations)
            {
                await transport.MapAsync(app, registration).ConfigureAwait(false);
            }
        }
    }
}
