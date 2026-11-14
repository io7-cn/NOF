namespace NOF.Infrastructure;

/// <summary>Accepts one independent durable receipt per stable subscription.</summary>
public sealed class MemoryNotificationRider(MemoryEndpointRegistry bindings, MessageInboxReceiver receiver)
{
    /// <summary>Submits an encoded transport envelope.</summary>
    public async Task PublishAsync(ReadOnlyMemory<byte> payload, string messageRoute,
        IEnumerable<KeyValuePair<string, string?>>? headers, CancellationToken cancellationToken = default)
    {
        var messageId = MessageInboxReceiver.GetMessageId(headers);
        foreach (var binding in bindings.Endpoints.Where(binding => binding.IsSubscription && binding.Route == messageRoute))
        {
            await receiver.ReceiveAsync(messageId, InboxMessageType.Notification, payload, binding.ConsumerId, headers, cancellationToken).ConfigureAwait(false);
        }
    }
}
