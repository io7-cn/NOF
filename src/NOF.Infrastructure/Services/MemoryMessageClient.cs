using NOF.Abstraction;

namespace NOF.Infrastructure;

/// <summary>Explicit Memory client; queue/topic selection belongs to its caller, not application operations.</summary>
public sealed class MemoryMessageClient(IObjectSerializer serializer, MessageDelivery delivery,
    MemoryEndpointRegistry bindings, MessageInboxReceiver receiver)
{
    /// <summary>Waits for acceptance by the selected queue.</summary>
    public Task SendAsync<T>(string queue, T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(queue, message, context, false, null, cancellationToken);
    /// <summary>Publishes to a selected topic.</summary>
    public Task PublishAsync<T>(string topic, T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(topic, message, context, true, null, cancellationToken);
    /// <summary>Delivers an outbox intent with its original idempotency and header metadata.</summary>
    public Task SendAsync<T>(string queue, T message, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(queue, message, context.Context, false, context, cancellationToken);
    /// <summary>Publishes an outbox intent with its original idempotency and header metadata.</summary>
    public Task PublishAsync<T>(string topic, T message, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(topic, message, context.Context, true, context, cancellationToken);

    private Task DispatchAsync<T>(string route, T message, Context context, bool publish,
        OutboxDeliveryContext? intent, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        return delivery.ExecuteAsync(message, context, async (headers, ct) =>
        {
            var payload = serializer.Serialize(message, typeof(T));
            if (publish)
            {
                var messageId = MessageInboxReceiver.GetMessageId(headers);
                foreach (var binding in bindings.Endpoints.Where(binding => binding.IsSubscription && binding.Route == route))
                {
                    await receiver.ReceiveAsync(messageId, payload, binding.ConsumerId, headers, ct).ConfigureAwait(false);
                }
            }
            else
            {
                var binding = bindings.Endpoints.SingleOrDefault(binding => !binding.IsSubscription && binding.Route == route)
                    ?? throw new InvalidOperationException($"No local queue binding for '{route}'.");
                await receiver.ReceiveAsync(MessageInboxReceiver.GetMessageId(headers), payload, binding.ConsumerId, headers, ct).ConfigureAwait(false);
            }
        }, cancellationToken, intent).AsTask();
    }
}
