using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Validates the selected default engine before any hosted service starts.</summary>
public sealed class InvocationPlanValidationService(InvocationPlanFactory plans) : IHostedLifecycleService
{
    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        plans.ValidateConfiguration();
        return Task.CompletedTask;
    }    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
