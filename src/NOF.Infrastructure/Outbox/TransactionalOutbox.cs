using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Snapshots intents into the provider session without dispatching or committing them.</summary>
public sealed class TransactionalOutbox(OutboxRegistry registry, IOutboxStore store,
    IObjectSerializer serializer) : IOutbox
{
    /// <inheritdoc />
    public ValueTask<Guid> EnqueueAsync<TMessage>(TMessage message, Context context,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var registration = registry.Get(typeof(TMessage));
        var tenant = context.TryGetItem(NOFAbstractionConstants.Transport.Headers.TenantId, out _)
            ? context.TenantId : Context.Current.TenantId;
        if (tenant != Context.Current.TenantId)
        {
            throw new InvalidOperationException("An outbox intent must use the tenant of its current unit of work.");
        }
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Key, NOFAbstractionConstants.Transport.Headers.MessageId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, NOFAbstractionConstants.Transport.Headers.TenantId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, NOFAbstractionConstants.Transport.Headers.TraceParent, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, NOFAbstractionConstants.Transport.Headers.Context, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Message ID, tenant, trace and context headers are managed by NOF.", nameof(headers));
                }
                metadata.Add(header.Key, header.Value);
            }
        }
        metadata[NOFAbstractionConstants.Transport.Headers.TenantId] = tenant;
        metadata[NOFAbstractionConstants.Transport.Headers.Context] = context.WithTenantId(tenant).Items.ToJsonString();
        var traceParent = Activity.Current?.ToTraceParent();
        metadata[NOFAbstractionConstants.Transport.Headers.TraceParent] = traceParent;
        var record = NOFOutboxMessage.Create(registration.MessageType,
            serializer.Serialize(message, typeof(TMessage)).ToArray(), string.Empty, traceParent);
        metadata[NOFAbstractionConstants.Transport.Headers.MessageId] = record.Id.ToString();
        record.Headers = serializer.SerializeToText(metadata, typeof(Dictionary<string, string?>));
        store.Stage(record);
        return ValueTask.FromResult(record.Id);
    }
}
