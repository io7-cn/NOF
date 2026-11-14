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
    private readonly IOptions<DeploymentOptions> _deployment;
    private readonly TransactionalMessageProcessorOptions _options;
    private readonly ILogger<InboxMessageBackgroundService> _logger;
    private readonly IObjectSerializer _objectSerializer;
    private readonly ITenantProvider _tenantProvider;

    public InboxMessageBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<DeploymentOptions> deployment,
        IOptions<TransactionalMessageOptions> options,
        ILogger<InboxMessageBackgroundService> logger,
        IObjectSerializer objectSerializer,
        ITenantProvider tenantProvider)
    {
        _serviceProvider = serviceProvider;
        _deployment = deployment;
        _options = options.Value.Inbox;
        _logger = logger;
        _objectSerializer = objectSerializer;
        _tenantProvider = tenantProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RunWorkers)
        { return; }

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
            using var ambientBinding = claimScope.ServiceProvider.BindNOF(Context.Current);
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

        foreach (var message in pendingMessages)
        {
            try
            {
                if (await ProcessSingleMessageAsync(message, cancellationToken))
                {
                    succeededCount++;
                }
                else
                {
                    failedCount++;
                }
            }
            catch (Exception)
            {
                failedCount++;
                // Failure details are recorded in the inbox row (retry/failed) and logged at the decision point.
            }
        }

        _logger.LogInformation(
            "Inbox batch processed: {Succeeded} processed, {Failed} failed",
            succeededCount,
            failedCount);
    }

    private async Task<bool> ProcessSingleMessageAsync(
        NOFInboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Status != InboxMessageStatus.Pending)
        {
            return false;
        }

        if (message.RetryCount > _options.MaxRetryCount)
        {
            await MarkFailedAsync(message, "Exceeded max retry count", cancellationToken);
            return false;
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        using var ambientBinding = scope.ServiceProvider.BindNOF(Context.Current);
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
                return false;
            }

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

            using var activity = StartBoundaryActivity(message, traceParent);

            var outcome = await services.GetRequiredKeyedService<IInboxHandler>(pendingMessage.Route).HandleAsync(
                pendingMessage.Payload, processingHeaders, cancellationToken);

            if (outcome is IFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                await MarkFailedAsync(message, outcome.ToString() ?? "Invocation declined", cancellationToken);
                return false;
            }

            pendingMessage.Status = InboxMessageStatus.Processed;
            pendingMessage.ProcessedAtUtc = DateTime.UtcNow;
            pendingMessage.ErrorMessage = null;
            pendingMessage.ClaimedBy = null;
            pendingMessage.ClaimExpiresAtUtc = null;

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
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

    private async Task MarkRetryAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        using var ambientBinding = scope.ServiceProvider.BindNOF(Context.Current);
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().MarkRetryAsync(message, error, cancellationToken);
    }
    private async Task MarkFailedAsync(NOFInboxMessage message, string error, CancellationToken cancellationToken, Exception? ex = null)
    {
        using var scope = _serviceProvider.CreateScope();
        using var ambientBinding = scope.ServiceProvider.BindNOF(Context.Current);
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().MarkFailedAsync(message, error, cancellationToken, ex);
    }
}
