using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure;
namespace NOF.Infrastructure;

/// <summary>
/// Outbox cleanup service that periodically removes old sent messages to maintain database performance.
/// </summary>
public sealed class OutboxCleanupBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OutboxCleanupBackgroundService> _logger;
    private readonly TransactionalMessageProcessorOptions _options;
    private readonly IOptions<DeploymentOptions> _deployment;
    private readonly ITenantProvider _tenantProvider;

    public OutboxCleanupBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<TransactionalMessageOptions> options,
        ILogger<OutboxCleanupBackgroundService> logger,
        IOptions<DeploymentOptions> deployment,
        ITenantProvider tenantProvider)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value.Outbox;
        _logger = logger;
        _deployment = deployment;
        _tenantProvider = tenantProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RunWorkers)
        { return; }

        _logger.LogInformation(
            "Outbox cleanup service started. Cleanup interval: {Interval}, Retention period: {Retention}",
            _options.CleanupInterval, _options.RetentionPeriod);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CleanupInterval, stoppingToken);
                if (!_deployment.Value.IsPrimaryNode)
                {
                    _logger.LogDebug(
                        "Skipping outbox cleanup on non-primary node {InstanceId}",
                        _deployment.Value.InstanceId);
                    continue;
                }

                await foreach (var tenantId in TransactionalMessageTenants.EnumerateAsync(_tenantProvider, stoppingToken))
                {
                    using var tenantScope = TransactionalMessageTenants.Push(tenantId);
                    try
                    {
                        await CleanupOutboxAsync(stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Error cleaning outbox for tenant {TenantId}", tenantId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during outbox cleanup");
            }
        }

        _logger.LogInformation("Outbox cleanup service stopped");
    }

    private async Task CleanupOutboxAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        using var ambientBinding = scope.ServiceProvider.BindNOF(Context.Current);
        var store = scope.ServiceProvider.GetService<IOutboxStore>();
        if (store is null)
        {
            _logger.LogDebug("Skipping outbox cleanup because no IOutboxStore is registered.");
            return;
        }
        var olderThan = DateTime.UtcNow - _options.RetentionPeriod;
        var (deletedCount, compactedOrderStateCount, deletedCompletedOrderStateCount) = await store.CleanupAsync(olderThan, cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Outbox cleanup completed. Deleted {Count} sent messages older than {Date}",
                deletedCount, olderThan);
        }
        else
        {
            _logger.LogDebug("Outbox cleanup completed. No messages to delete");
        }

        if (compactedOrderStateCount > 0 || deletedCompletedOrderStateCount > 0)
        {
            _logger.LogInformation(
                "Outbox order-state cleanup completed. Compacted {CompactedCount} historical allocations and deleted {CompletedCount} completed keys",
                compactedOrderStateCount,
                deletedCompletedOrderStateCount);
        }
    }
}
