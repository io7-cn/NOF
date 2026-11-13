using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;
using System.Diagnostics;

namespace NOF.Infrastructure;

public sealed class OutboxMessageBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TransactionalMessageProcessorOptions _options;
    private readonly ILogger<OutboxMessageBackgroundService> _logger;
    private readonly IObjectSerializer _objectSerializer;
    private readonly IOptions<DeploymentOptions> _deployment;
    private readonly ITenantProvider _tenantProvider;

    public OutboxMessageBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<TransactionalMessageOptions> options,
        ILogger<OutboxMessageBackgroundService> logger,
        IObjectSerializer objectSerializer,
        IOptions<DeploymentOptions> deployment,
        ITenantProvider tenantProvider)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value.Outbox;
        _logger = logger;
        _objectSerializer = objectSerializer;
        _deployment = deployment;
        _tenantProvider = tenantProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox message sender started. PollingInterval: {Interval}, BatchSize: {BatchSize}, MaxRetry: {MaxRetry}",
            _options.PollingInterval, _options.BatchSize, _options.MaxRetryCount);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
                await foreach (var tenantId in TransactionalMessageTenants.EnumerateAsync(_tenantProvider, stoppingToken))
                {
                    using var tenantScope = TransactionalMessageTenants.Push(tenantId);
                    try
                    {
                        await ProcessPendingMessagesAsync(stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Error processing outbox messages for tenant {TenantId}", tenantId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in outbox background service loop");
            }
        }

        _logger.LogInformation("Outbox message sender stopped");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var store = scope.ServiceProvider.GetService<IOutboxStore>();
        if (store is null)
        {
            _logger.LogDebug("Skipping outbox processing because no IOutboxStore provider is registered.");
            return;
        }
        var commandRider = scope.ServiceProvider.GetRequiredService<ICommandRider>();
        var notificationRider = scope.ServiceProvider.GetRequiredService<INotificationRider>();

        try
        {
            var pendingMessages = await store.AtomicClaimPendingMessagesAsync(_options.BatchSize, _options.ClaimTimeout, cancellationToken)
                .ToListAsync(cancellationToken);

            if (pendingMessages.Count == 0)
            {
                return;
            }

            _logger.LogDebug("Claimed {Count} pending outbox messages", pendingMessages.Count);
            await ProcessMessagesBatch(store, pendingMessages, commandRider, notificationRider, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing outbox messages");
        }
    }

    private async Task<bool> ProcessSingleMessageAsync(
        IOutboxStore store,
        NOFOutboxMessage message,
        ICommandRider commandRider,
        INotificationRider notificationRider,
        CancellationToken cancellationToken)
    {
        if (message.RetryCount > _options.MaxRetryCount)
        {
            await store.AtomicRecordDeliveryFailureAsync(message, $"Exceeded max retry count ({_options.MaxRetryCount})", cancellationToken);
            _logger.LogWarning("Message {MessageId} exceeded max retry count ({MaxRetry}), marked as failed",
                message.Id, _options.MaxRetryCount);
            return false;
        }

        // Restore the tracing context
        using var activity = RestoreTracingContext(message);
        var dispatchRoutes = ResolveDispatchRoutes(message);
        var headers = string.IsNullOrWhiteSpace(message.Headers)
            ? new Dictionary<string, string?>()
            : _objectSerializer.Deserialize<Dictionary<string, string?>>(message.Headers) ?? new Dictionary<string, string?>();
        AddOrderingHeaders(message, headers);
        var tenantId = TenantId.Normalize(Context.Current.TenantId);
        headers[NOFAbstractionConstants.Transport.Headers.TenantId] = tenantId;

        // Restore the ambient execution context for downstream components that rely on it.
        // This keeps "deferred send" semantics consistent: we persist the execution context snapshot,
        // and restore it when actually dispatching the outbox message.
        using var _ = Context.PushCurrent(Context.Empty.WithTenantId(tenantId));

        activity?.SetTag(NOFInfrastructureConstants.OutboundPipeline.Tags.MessageId, message.Id.ToString());
        activity?.SetTag(NOFInfrastructureConstants.OutboundPipeline.Tags.MessageType, dispatchRoutes[0]);
        if (headers.TryGetValue(NOFAbstractionConstants.Transport.Headers.TenantId, out var activityTenantId))
        {
            activity?.SetTag(NOFInfrastructureConstants.OutboundPipeline.Tags.TenantId, activityTenantId);
        }

        try
        {
            switch (message.MessageType)
            {
                case OutboxMessageType.Command:
                    await commandRider.SendAsync(
                        message.Payload,
                        dispatchRoutes[0],
                        headers,
                        cancellationToken);
                    _logger.LogDebug("Sent command via rider {MessageId} of route {Route} (retry {Retry})",
                        message.Id, dispatchRoutes[0], message.RetryCount);
                    break;
                case OutboxMessageType.Notification:
                    await notificationRider.PublishAsync(
                        message.Payload,
                        dispatchRoutes[0],
                        headers,
                        cancellationToken);
                    _logger.LogDebug("Published notification via rider {MessageId} with {RouteCount} routes (retry {Retry})",
                        message.Id, dispatchRoutes.Length, message.RetryCount);
                    break;
                default:
                    await store.AtomicRecordDeliveryFailureAsync(message, "Unsupported message type", cancellationToken);
                    _logger.LogError("Message {MessageId} has unsupported message type: {Type}",
                        message.Id, message.MessageType);
                    return false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Service is shutting down, do not record as failure; leave for retry on next startup
            _logger.LogInformation("Message {MessageId} delivery canceled due to shutdown", message.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);
            throw; // rethrow to prevent marking as succeeded
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deliver message {MessageId} (retry {Retry})", message.Id,
                message.RetryCount);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await store.AtomicRecordDeliveryFailureAsync(message, ex.Message, cancellationToken);
            throw; // ensure not added to success list
        }

        // Processing completed successfully, set Activity status to OK
        activity?.SetStatus(ActivityStatusCode.Ok);
        return true;
    }

    private static void AddOrderingHeaders(
        NOFOutboxMessage message,
        IDictionary<string, string?> headers)
    {
        foreach (var reservedHeader in headers.Keys.Where(static key =>
                     string.Equals(key, NOFAbstractionConstants.Transport.Headers.OrderKey, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(key, NOFAbstractionConstants.Transport.Headers.Sequence, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(key, NOFAbstractionConstants.Transport.Headers.CompletesOrderKey, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            headers.Remove(reservedHeader);
        }

        if (message.OrderKey is null && message.Sequence is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message.OrderKey) || message.Sequence is null or <= 0)
        {
            throw new InvalidOperationException(
                $"Outbox message '{message.Id}' must have both a non-empty order key and a positive sequence, or neither.");
        }

        headers[NOFAbstractionConstants.Transport.Headers.OrderKey] = message.OrderKey;
        headers[NOFAbstractionConstants.Transport.Headers.Sequence] = message.Sequence.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (message.CompletesOrderKey)
        {
            headers[NOFAbstractionConstants.Transport.Headers.CompletesOrderKey] = bool.TrueString;
        }
    }

    private Activity? RestoreTracingContext(NOFOutboxMessage message)
    {
        var dispatchRoutes = ResolveDispatchRoutes(message);
        return NOFInfrastructureConstants.OutboundPipeline.Source.StartActivityWithParent(
            $"{NOFInfrastructureConstants.OutboundPipeline.ActivityNames.MessageSending}: {dispatchRoutes[0]}",
            ActivityKind.Producer,
            message.TraceParent,
            _deployment.Value);
    }

    private string[] ResolveDispatchRoutes(NOFOutboxMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.DispatchRoutes))
        {
            throw new InvalidOperationException($"Outbox message '{message.Id}' does not contain any dispatch routes.");
        }

        var routes = _objectSerializer.Deserialize<string[]>(message.DispatchRoutes);
        if (routes is null || routes.Length == 0)
        {
            throw new InvalidOperationException($"Outbox message '{message.Id}' does not contain any dispatch routes.");
        }

        return routes;
    }

    private async Task ProcessMessagesBatch(
        IOutboxStore store,
        IReadOnlyCollection<NOFOutboxMessage> pendingMessages,
        ICommandRider commandRider,
        INotificationRider notificationRider,
        CancellationToken cancellationToken)
    {
        var succeededIds = new List<Guid>(pendingMessages.Count);
        var failedCount = 0;

        var messages = pendingMessages.ToArray();
        for (var index = 0; index < messages.Length; index++)
        {
            var message = messages[index];
            try
            {
                var succeeded = await ProcessSingleMessageAsync(
                    store,
                    message,
                    commandRider,
                    notificationRider,
                    cancellationToken);
                if (succeeded)
                {
                    succeededIds.Add(message.Id);
                    continue;
                }

                failedCount++;

                // Do not allow later messages in the ordered batch to overtake a failed predecessor.
                var unprocessedIds = messages.Skip(index + 1).Select(static pending => pending.Id).ToArray();
                if (unprocessedIds.Length > 0)
                {
                    await store.AtomicReleaseClaimsAsync(unprocessedIds, cancellationToken);
                }

                break;
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(ex, "Unhandled exception while processing claimed message {MessageId}", message.Id);

                // Do not allow later messages in the ordered batch to overtake a failed predecessor.
                var unprocessedIds = messages.Skip(index + 1).Select(static pending => pending.Id).ToArray();
                if (unprocessedIds.Length > 0)
                {
                    await store.AtomicReleaseClaimsAsync(unprocessedIds, cancellationToken);
                }

                break;
            }
        }

        if (succeededIds.Count == 0)
        {
            _logger.LogInformation(
                "Outbox batch processed: {Succeeded} sent, {Failed} failed",
                0,
                failedCount);
            return;
        }

        try
        {
            var sentCount = await store.AtomicMarkAsSentAsync(succeededIds, cancellationToken);
            if (sentCount != succeededIds.Count)
            {
                var notMarkedAsSent = succeededIds.Count - sentCount;
                failedCount += notMarkedAsSent;
                _logger.LogWarning(
                    "Failed to mark {Count} delivered outbox messages as sent because they were no longer pending",
                    notMarkedAsSent);
            }

            _logger.LogInformation(
                "Outbox batch processed: {Succeeded} sent, {Failed} failed",
                sentCount,
                failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark {Count} claimed messages as sent", succeededIds.Count);
        }
    }

}
