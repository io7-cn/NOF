using NOF.Abstraction;

namespace NOF.Infrastructure;

/// <summary>Explicit Memory client; queue/topic selection belongs to its caller, not application operations.</summary>
public sealed class MemoryMessageClient(IObjectSerializer serializer, MessageDelivery delivery,
    MemoryCommandRider commands, MemoryNotificationRider notifications)
{
    /// <summary>Waits for acceptance by the selected queue.</summary>
    public Task SendAsync<T>(string queue, T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(queue, message, context, false, null, cancellationToken);
    /// <summary>Publishes to a selected topic.</summary>
    public Task PublishAsync<T>(string topic, T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(topic, message, context, true, null, cancellationToken);
    /// <summary>Delivers an outbox intent with its original idempotency and ordering metadata.</summary>
    public Task SendAsync<T>(string queue, T message, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(queue, message, context.Context, false, context, cancellationToken);
    /// <summary>Publishes an outbox intent with its original idempotency and ordering metadata.</summary>
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
                await notifications.PublishAsync(payload, route, headers, ct).ConfigureAwait(false);
            }
            else
            {
                await commands.SendAsync(payload, route, headers, ct).ConfigureAwait(false);
            }
        }, cancellationToken, intent).AsTask();
    }
}
