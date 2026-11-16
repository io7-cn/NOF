using Microsoft.Extensions.Hosting;
using NOF.Abstraction;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Completes host database migrations before any hosted service starts, including HTTP and consumers.</summary>
public sealed class DatabaseMigrationHostedService(IServiceProvider services, IEnumerable<DbContextMigration> migrations) : IHostedLifecycleService
{
    /// <inheritdoc />
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        foreach (var migration in migrations)
        {
            await migration.MigrateTenantAsync(services, NOFAbstractionConstants.Tenant.HostId, cancellationToken).ConfigureAwait(false);
        }
    }
    /// <inheritdoc />
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
