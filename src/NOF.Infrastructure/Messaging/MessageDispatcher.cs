using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Resolves host-owned routes and uses the selected immediate transports or transactional outbox.</summary>
public sealed class MessageDispatcher(ICommandRider commands, INotificationRider notifications,
    MessageBindingRegistry bindings, MessageDelivery delivery, IOutboxStore outbox,
    IObjectSerializer serializer, OutboxOrderSequenceAllocator sequences) : IMessageDispatcher
{
    /// <inheritdoc />
    public Task SendAsync<T>(ServiceOperationReference<T> operation, T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(message, bindings.GetQueue(operation), false, false, null, context, false, cancellationToken);
    /// <inheritdoc />
    public Task DeferSendAsync<T>(ServiceOperationReference<T> operation, T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(message, bindings.GetQueue(operation), false, true, null, context, false, cancellationToken);
    /// <inheritdoc />
    public Task DeferSendOrderedAsync<T>(ServiceOperationReference<T> operation, T message, string orderKey, Context context,
        bool completesOrderKey = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderKey);
        return DispatchAsync(message, bindings.GetQueue(operation), false, true, orderKey, context, completesOrderKey, cancellationToken);
    }
    /// <inheritdoc />
    public Task PublishAsync<T>(T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(message, bindings.GetTopic(typeof(T)), true, false, null, context, false, cancellationToken);
    /// <inheritdoc />
    public Task DeferPublishAsync<T>(T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(message, bindings.GetTopic(typeof(T)), true, true, null, context, false, cancellationToken);
    /// <inheritdoc />
    public Task DeferPublishOrderedAsync<T>(T message, string orderKey, Context context,
        bool completesOrderKey = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderKey);
        return DispatchAsync(message, bindings.GetTopic(typeof(T)), true, true, orderKey, context, completesOrderKey, cancellationToken);
    }

    private Task DispatchAsync<T>(T message, string route, bool publish, bool deferred, string? orderKey,
        Context context, bool completesOrderKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return delivery.ExecuteAsync(message, context, async (headers, ct) =>
        {
            var payload = serializer.Serialize(message, typeof(T));
            if (deferred)
            {
                var order = orderKey is null ? (OutboxOrder?)null
                    : await sequences.AllocateAsync(orderKey, completesOrderKey, ct).ConfigureAwait(false);
                outbox.Stage(NOFOutboxMessage.Create(publish ? OutboxMessageType.Notification : OutboxMessageType.Command,
                    serializer.SerializeToText(new[] { route }, typeof(string[])), payload.ToArray(),
                    serializer.SerializeToText(headers, typeof(Dictionary<string, string?>)), Activity.Current?.ToTraceParent(), order));
            }
            else if (publish)
            {
                await notifications.PublishAsync(payload, route, headers, ct).ConfigureAwait(false);
            }
            else
            {
                await commands.SendAsync(payload, route, headers, ct).ConfigureAwait(false);
            }
        }, cancellationToken).AsTask();
    }
}
