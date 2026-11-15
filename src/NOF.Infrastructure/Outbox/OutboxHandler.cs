namespace NOF.Infrastructure;

/// <summary>
/// Delivers one durable message type through infrastructure-owned clients.
/// Message identity is inferred from TMessage; no message-name or schema-version declaration is required.
/// Generated application-part registration reads the message type without constructing handlers or enabling Outbox workers.
/// </summary>
/// <typeparam name="TMessage">The single message type handled by this adapter.</typeparam>
public abstract class OutboxHandler<TMessage>
{
    /// <summary>Returns only after the external operation has met its documented acceptance conditions.</summary>
    public abstract ValueTask HandleAsync(TMessage message, OutboxDeliveryContext context, CancellationToken cancellationToken);
}
