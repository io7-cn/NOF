using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Hosting;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Claims generic intents and invokes scoped handlers independently of all transport clients.</summary>
public sealed class OutboxMessageBackgroundService(IServiceProvider services, IOptions<TransactionalMessageOptions> options,
    ILogger<OutboxMessageBackgroundService> logger, IOptions<DeploymentOptions> deployment, ITenantProvider tenants) : BackgroundService
{
    private readonly TransactionalMessageProcessorOptions _options = options.Value.Outbox;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.RunWorkers)
        { return; }

        logger.LogInformation("Outbox intent processor started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
                await foreach (var tenant in TransactionalMessageTenants.EnumerateAsync(tenants, stoppingToken))
                {
                    using var tenantScope = TransactionalMessageTenants.Push(tenant);
                    try
                    {
                        await ProcessPendingMessagesAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        logger.LogError(error, "Error processing outbox intents for tenant {TenantId}", tenant);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Unexpected error in outbox processor");
            }
        }
        logger.LogInformation("Outbox intent processor stopped");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        using var ambientBinding = scope.ServiceProvider.BindNOF(Context.Current);
        var store = scope.ServiceProvider.GetService<IOutboxStore>();
        if (store is null)
        {
            return;
        }
        var messages = await store.AtomicClaimPendingMessagesAsync(_options.BatchSize, _options.ClaimTimeout, cancellationToken)
            .ToListAsync(cancellationToken);
        var succeeded = 0;
        var failed = 0;
        for (var index = 0; index < messages.Count; index++)
        {
            var record = messages[index];
            using var activity = NOFInfrastructureConstants.OutboundPipeline.Source.StartActivityWithParent(
                $"Outbox: {record.MessageType}", ActivityKind.Producer, record.TraceParent, deployment.Value);
            activity?.SetTag("messaging.message.id", record.Id.ToString());
            try
            {
                await DeliverAsync(record, cancellationToken);
                activity?.SetStatus(ActivityStatusCode.Ok);
                succeeded += await store.AtomicMarkAsSentAsync([record], cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A possibly completed side effect is retried with the same ID after lease expiry.
                // Unstarted records are safe to release immediately without consuming an attempt.
                await store.AtomicReleaseClaimsAsync(messages.Skip(index + 1), CancellationToken.None);
                throw;
            }
            catch (Exception error)
            {
                failed++;
                activity?.SetStatus(ActivityStatusCode.Error, error.Message);
                logger.LogWarning(error, "Outbox message {MessageId} attempt {Attempt} failed", record.Id, record.RetryCount);
                await store.AtomicRecordDeliveryFailureAsync(record, error.Message,
                    error is OutboxPermanentFailureException, cancellationToken);
            }
        }
        if (messages.Count != 0)
        {
            logger.LogInformation("Outbox batch processed: {Succeeded} sent, {Failed} failed", succeeded, failed);
        }
    }

    private async ValueTask DeliverAsync(NOFOutboxMessage record, CancellationToken cancellationToken)
    {
        var tenant = Context.Current.TenantId;
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId(tenant));
        await using var scope = services.CreateAsyncScope();
        using var ambientBinding = scope.ServiceProvider.BindNOF(Context.Current);
        var serializer = scope.ServiceProvider.GetRequiredService<IObjectSerializer>();
        var headers = serializer.Deserialize<Dictionary<string, string?>>(record.Headers)
            ?? throw new OutboxPermanentFailureException("Missing outbox metadata.");
        var persistedTenant = headers.FirstOrDefault(pair => string.Equals(pair.Key,
            NOFAbstractionConstants.Transport.Headers.TenantId, StringComparison.OrdinalIgnoreCase)).Value;
        if (TenantId.Normalize(persistedTenant) != tenant)
        {
            throw new OutboxPermanentFailureException("Outbox metadata does not match its persistence tenant.");
        }
        var registration = scope.ServiceProvider.GetRequiredService<OutboxRegistry>().Get(record.MessageType, record.SchemaVersion);
        var context = new OutboxDeliveryContext
        {
            MessageId = record.Id,
            Context = Context.Current,
            Attempt = record.RetryCount,
            Headers = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>(
                new Dictionary<string, string?>(headers, StringComparer.OrdinalIgnoreCase))
        };
        await registration.Deliver(scope.ServiceProvider, record, context, cancellationToken).ConfigureAwait(false);
    }
}
