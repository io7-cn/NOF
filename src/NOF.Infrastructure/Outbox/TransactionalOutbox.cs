using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Snapshots intents into the provider session without dispatching or committing them.</summary>
public sealed class TransactionalOutbox(OutboxRegistry registry, IOutboxStore store,
    IObjectSerializer serializer, IServiceProvider services) : IOutbox
{
    /// <inheritdoc />
    public async ValueTask<Guid> EnqueueAsync<TMessage>(TMessage message, Context context,
        OutboxEnqueueOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (options is { OrderKey: null, CompletesOrderKey: true })
        {
            throw new ArgumentException("Completing a stream requires an order key.", nameof(options));
        }
        var registration = registry.Get(typeof(TMessage));
        var payload = serializer.Serialize(message, typeof(TMessage)).ToArray();
        var tenant = context.TryGetItem(NOFAbstractionConstants.Transport.Headers.TenantId, out var supplied)
            && supplied is string requested ? TenantId.Normalize(requested) : Context.Current.TenantId;
        if (tenant != Context.Current.TenantId)
        {
            throw new InvalidOperationException("An outbox intent must use the tenant of its current unit of work.");
        }
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [NOFAbstractionConstants.Transport.Headers.TenantId] = tenant
        };
        // Credentials and arbitrary Context.Items are deliberately not persisted.
        var metadata = serializer.SerializeToText(headers, typeof(Dictionary<string, string?>));
        var order = options?.OrderKey is { } key
            ? await services.GetRequiredService<OutboxOrderSequenceAllocator>()
                .AllocateAsync(key, options.CompletesOrderKey, cancellationToken).ConfigureAwait(false)
            : (OutboxOrder?)null;
        var record = NOFOutboxMessage.Create(registration.MessageType, registration.SchemaVersion,
            payload, metadata, Activity.Current?.ToTraceParent(), order);
        store.Stage(record);
        return record.Id;
    }
}
