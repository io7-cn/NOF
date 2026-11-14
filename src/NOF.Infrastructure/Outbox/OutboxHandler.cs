namespace NOF.Infrastructure;

/// <summary>
/// Delivers one durable message type through infrastructure-owned clients.
/// Concrete handlers declare a public const string MessageType and optionally a public const int SchemaVersion (default 1).
/// Generated application-part registration reads these constants without constructing handlers or enabling Outbox workers.
/// </summary>
/// <typeparam name="TMessage">The single message type handled by this adapter.</typeparam>
public abstract class OutboxHandler<TMessage>
{
    /// <summary>Returns only after the external operation has met its documented acceptance conditions.</summary>
    public abstract ValueTask HandleAsync(TMessage message, OutboxDeliveryContext context, CancellationToken cancellationToken);
}
