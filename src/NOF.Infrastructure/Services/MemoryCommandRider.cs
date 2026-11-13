namespace NOF.Infrastructure;

/// <summary>Submits a selected queue binding to the durable in-memory inbox transport.</summary>
public sealed class MemoryCommandRider(MessageBindingRegistry bindings, MessageInboxReceiver receiver) : ICommandRider
{
    /// <inheritdoc />
    public Task SendAsync(ReadOnlyMemory<byte> payload, string messageRoute,
        IEnumerable<KeyValuePair<string, string?>>? headers, CancellationToken cancellationToken = default)
    {
        var binding = bindings.Bindings.SingleOrDefault(binding => !binding.IsSubscription && binding.Route == messageRoute)
            ?? throw new InvalidOperationException($"No local queue binding for '{messageRoute}'.");
        return receiver.ReceiveAsync(MessageInboxReceiver.GetMessageId(headers), InboxMessageType.Command,
            payload, binding.ConsumerId, headers, cancellationToken);
    }
}
