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

public sealed class InboxMessageBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CommandHandlerRegistry _commandHandlerRegistry;
    private readonly NotificationHandlerRegistry _notificationHandlerRegistry;
    private readonly IOptions<DeploymentOptions> _deployment;
    private readonly TransactionalMessageProcessorOptions _options;
    private readonly ILogger<InboxMessageBackgroundService> _logger;
    private readonly IObjectSerializer _objectSerializer;
    private readonly ITenantProvider _tenantProvider;

    public InboxMessageBackgroundService(
        IServiceProvider serviceProvider,
        CommandHandlerRegistry commandHandlerRegistry,
        NotificationHandlerRegistry notificationHandlerRegistry,
        IOptions<DeploymentOptions> deployment,
        IOptions<TransactionalMessageOptions> options,
        ILogger<InboxMessageBackgroundService> logger,
        IObjectSerializer objectSerializer,
        ITenantProvider tenantProvider)
    {
        _serviceProvider = serviceProvider;
        _commandHandlerRegistry = commandHandlerRegistry;
        _notificationHandlerRegistry = notificationHandlerRegistry;
        _deployment = deployment;
        _options = options.Value.Inbox;
        _logger = logger;
        _objectSerializer = objectSerializer;
        _tenantProvider = tenantProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inbox message processor started. PollingInterval: {Interval}, BatchSize: {BatchSize}, MaxRetry: {MaxRetry}",
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
                        _logger.LogError(ex, "Error processing inbox messages for tenant {TenantId}", tenantId);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in inbox background service loop");
            }
        }

        _logger.LogInformation("Inbox message processor stopped");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        List<NOFInboxMessage> pendingMessages;
        using (var claimScope = _serviceProvider.CreateScope())
        {
            claimScope.ServiceProvider.ResolveDaemonServices();
            var store = claimScope.ServiceProvider.GetService<IInboxStore>();
            if (store is null)
            {
                _logger.LogDebug("Skipping inbox processing because no IInboxStore is registered.");
                return;
            }

            pendingMessages = await store.AtomicClaimPendingMessagesAsync(_options.BatchSize, _options.ClaimTimeout, cancellationToken)
                .ToListAsync(cancellationToken);
        }

        if (pendingMessages.Count == 0)
        {
            return;
        }

        var succeededCount = 0;
        var failedCount = 0;

        foreach (var message in pendingMessages.Where(static message => message.OrderKey is null))
        {
            try
            {
                await ProcessSingleMessageAsync(message, cancellationToken);
                succeededCount++;
            }
            catch (Exception)
            {
                failedCount++;
                // Failure details are recorded in the inbox row (retry/failed) and logged at the decision point.
            }
        }

        foreach (var orderedGroup in pendingMessages
                     .Where(static message => message.OrderKey is not null)
                     .GroupBy(static message => (message.Route, message.OrderKey)))
        {
            var orderedMessages = orderedGroup.OrderBy(static message => message.Sequence).ToArray();
            var groupFailed = false;
            for (var index = 0; index < orderedMessages.Length; index++)
            {
                var message = orderedMessages[index];
                try
                {
                    await ProcessSingleMessageAsync(message, cancellationToken);
                    succeededCount++;
                }
                catch (Exception)
                {
                    failedCount++;
                    groupFailed = true;
                    await ReleaseUnprocessedOrderedMessagesAsync(
                        orderedMessages.Skip(index + 1),
                        cancellationToken);
                    break;
                }
            }

            await ReleaseOrderStateClaimAsync(
                orderedGroup.Key.Route,
                orderedGroup.Key.OrderKey!,
                orderedMessages[0].ClaimedBy,
                cancellationToken);

            if (groupFailed)
            {
                continue;
            }
        }

        _logger.LogInformation(
            "Inbox batch processed: {Succeeded} processed, {Failed} failed",
            succeededCount,
            failedCount);
    }

    private async Task ProcessSingleMessageAsync(
        NOFInboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Status != InboxMessageStatus.Pending)
        {
            return;
        }

        if (message.RetryCount > _options.MaxRetryCount)
        {
            await MarkFailedAsync(message, "Exceeded max retry count", cancellationToken);
            throw new InvalidOperationException($"Inbox message '{message.Id}' exceeded the maximum retry count.");
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IInboxStore>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        await using var transaction = await services.GetRequiredService<ITransactionManager>().BeginTransactionAsync(cancellationToken);

        try
        {
            var pendingMessage = await store.FindPendingAsync(message, cancellationToken);

            if (pendingMessage is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return;
            }

            var orderState = await store.LoadAndValidateOrderStateAsync(pendingMessage, cancellationToken);

            var headers = DeserializeHeaders(pendingMessage.Headers);
            var traceParent = ExtractTraceParent(headers);
            var processingHeaders = RemoveTraceParent(headers)
                .Where(static header => !string.Equals(
                    header.Key,
                    NOFAbstractionConstants.Transport.Headers.TenantId,
                    StringComparison.OrdinalIgnoreCase))
                .Append(new KeyValuePair<string, string?>(
                    NOFAbstractionConstants.Transport.Headers.TenantId,
                    TenantId.Normalize(Context.Current.TenantId)))
                .ToArray();
            var handlerTypeName = ResolveHandlerTypeName(pendingMessage);

            using var activity = StartBoundaryActivity(message, traceParent);

            switch (pendingMessage.MessageType)
            {
                case InboxMessageType.Command:
                    {
                        var commandPipelineExecutor = services.GetRequiredService<CommandDeliveryExecutor>();
                        await commandPipelineExecutor.ExecuteAsync(
                            message.Payload,
                            handlerTypeName,
                            processingHeaders,
                            cancellationToken);
                        break;
                    }
                case InboxMessageType.Notification:
                    {
                        var notificationPipelineExecutor = services.GetRequiredService<NotificationDeliveryExecutor>();
                        await notificationPipelineExecutor.ExecuteAsync(
                            message.Payload,
                            handlerTypeName,
                            processingHeaders,
                            cancellationToken);
                        break;
                    }
                default:
                    throw new InvalidOperationException($"Unsupported inbox message type '{pendingMessage.MessageType}'.");
            }

            pendingMessage.Status = InboxMessageStatus.Processed;
            pendingMessage.ProcessedAtUtc = DateTime.UtcNow;
            pendingMessage.ErrorMessage = null;
            pendingMessage.ClaimedBy = null;
            pendingMessage.ClaimExpiresAtUtc = null;

            if (orderState is not null)
            {
                orderState.NextSequence = checked(orderState.NextSequence + 1);
                orderState.UpdatedAtUtc = DateTime.UtcNow;
                orderState.ErrorMessage = null;
                if (pendingMessage.CompletesOrderKey)
                {
                    orderState.CompletedAtUtc = DateTime.UtcNow;
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);

            if (message.RetryCount >= _options.MaxRetryCount)
            {
                await MarkFailedAsync(message, ex.Message, cancellationToken, ex);
            }
            else
            {
                await MarkRetryAsync(message, ex.Message, cancellationToken);
            }

            throw;
        }
    }

    private IEnumerable<KeyValuePair<string, string?>> DeserializeHeaders(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<KeyValuePair<string, string?>>();
        }

        var dict = _objectSerializer.Deserialize<Dictionary<string, string?>>(raw) ?? new Dictionary<string, string?>();
        return dict;
    }

    private static string? ExtractTraceParent(IEnumerable<KeyValuePair<string, string?>> headers)
        => headers.FirstOrDefault(static kvp => kvp.Key == NOFAbstractionConstants.Transport.Headers.TraceParent).Value;

    private static IReadOnlyCollection<KeyValuePair<string, string?>> RemoveTraceParent(IEnumerable<KeyValuePair<string, string?>> headers)
        => [.. headers.Where(static kvp => !string.Equals(
            kvp.Key,
            NOFAbstractionConstants.Transport.Headers.TraceParent,
            StringComparison.OrdinalIgnoreCase))];

    private static Activity? StartBoundaryActivity(NOFInboxMessage message, string? traceParent)
        => NOFInfrastructureConstants.InboundPipeline.Source.StartActivityWithParent(
            $"InboundTransport: {message.Route}",
            ActivityKind.Consumer,
            traceParent);

    private string ResolveHandlerTypeName(NOFInboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Route);

        return message.MessageType switch
        {
            InboxMessageType.Command => ResolveCommandHandlerTypeName(message.Route),
            InboxMessageType.Notification => ResolveNotificationHandlerTypeName(message.Route),
            _ => throw new InvalidOperationException($"Unsupported inbox message type '{message.MessageType}'.")
        };
    }

    private string ResolveCommandHandlerTypeName(string route)
    {
        if (_commandHandlerRegistry.TryGetHandlerType(route, out var handlerType))
        {
            return handlerType.DisplayName;
        }

        var resolvedHandlerType = _commandHandlerRegistry.GetHandlers(route).FirstOrDefault()
            ?? throw new InvalidOperationException($"No command handler route is registered for '{route}'.");

        return resolvedHandlerType.DisplayName;
    }

    private string ResolveNotificationHandlerTypeName(string route)
    {
        if (_notificationHandlerRegistry.TryGetHandlerType(route, out var handlerType))
        {
            return handlerType.DisplayName;
        }

        foreach (var notificationGroup in _notificationHandlerRegistry.Freeze().GroupBy(static registration => registration.HandlerType))
        {
            var handlerRoute = BuildNotificationRoute(_deployment.Value.ServiceName, notificationGroup.Key.DisplayName);

            if (string.Equals(handlerRoute, route, StringComparison.Ordinal))
            {
                return notificationGroup.Key.DisplayName;
            }
        }

        throw new InvalidOperationException($"No notification handler route is registered for '{route}'.");
    }

    private static string BuildNotificationRoute(string? serviceName, string handlerDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerDisplayName);

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return handlerDisplayName;
        }

        return $"{serviceName}.{handlerDisplayName}";
    }
    private async Task MarkRetryAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.ResolveDaemonServices();
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().MarkRetryAsync(message, error, cancellationToken);
    }
    private async Task MarkFailedAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken, Exception? ex = null)
    {
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.ResolveDaemonServices();
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().MarkFailedAsync(message, error, cancellationToken, ex);
    }
    private async Task ReleaseUnprocessedOrderedMessagesAsync(IEnumerable<NOFInboxMessage> messages, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.ResolveDaemonServices();
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().ReleaseUnprocessedOrderedMessagesAsync(messages, cancellationToken);
    }
    private async Task ReleaseOrderStateClaimAsync(string route, string orderKey, string? claimedBy, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.ResolveDaemonServices();
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().ReleaseOrderStateClaimAsync(route, orderKey, claimedBy, cancellationToken);
    }
}
