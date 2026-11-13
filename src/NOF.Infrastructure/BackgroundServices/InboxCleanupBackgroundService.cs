using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure;

namespace NOF.Infrastructure;

/// <summary>
/// Inbox cleanup service that periodically removes old processed messages to maintain database performance.
/// </summary>
public sealed class InboxCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InboxCleanupBackgroundService> _logger;
    private readonly TransactionalMessageProcessorOptions _options;
    private readonly IOptions<DeploymentOptions> _deployment;
    private readonly ITenantProvider _tenantProvider;

    public InboxCleanupBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<TransactionalMessageOptions> options,
        ILogger<InboxCleanupBackgroundService> logger,
        IOptions<DeploymentOptions> deployment,
        ITenantProvider tenantProvider)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value.Inbox;
        _logger = logger;
        _deployment = deployment;
        _tenantProvider = tenantProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inbox cleanup service started. Cleanup interval: {Interval}, Retention period: {Retention}",
            _options.CleanupInterval, _options.RetentionPeriod);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CleanupInterval, stoppingToken);
                if (!_deployment.Value.IsPrimaryNode)
                {
                    _logger.LogDebug(
                        "Skipping inbox cleanup on non-primary node {InstanceId}",
                        _deployment.Value.InstanceId);
                    continue;
                }

                await foreach (var tenantId in TransactionalMessageTenants.EnumerateAsync(_tenantProvider, stoppingToken))
                {
                    using var tenantScope = TransactionalMessageTenants.Push(tenantId);
                    try
                    {
                        await CleanupInboxAsync(stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Error cleaning inbox for tenant {TenantId}", tenantId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during inbox cleanup");
            }
        }

        _logger.LogInformation("Inbox cleanup service stopped");
    }

    private async Task CleanupInboxAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var store = scope.ServiceProvider.GetService<IInboxStore>();
        if (store is null)
        {
            _logger.LogDebug("Skipping inbox cleanup because no IInboxStore is registered.");
            return;
        }
        var olderThan = DateTime.UtcNow - _options.RetentionPeriod;
        var (deletedCount, deletedOrderStateCount) = await store.CleanupAsync(olderThan, cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Inbox cleanup completed. Deleted {Count} processed messages older than {Date}",
                deletedCount, olderThan);
        }
        else
        {
            _logger.LogDebug("Inbox cleanup completed. No messages to delete");
        }

        if (deletedOrderStateCount > 0)
        {
            _logger.LogInformation(
                "Inbox cleanup deleted {Count} completed order states older than {Date}",
                deletedOrderStateCount,
                olderThan);
        }
    }
}
