namespace NOF.Infrastructure;

/// <summary>Interprets one durable intent using infrastructure-owned clients and success semantics.</summary>
public interface IOutboxHandler<in TMessage>
{
    /// <summary>Returns only after the external operation has met its documented acceptance conditions.</summary>
    ValueTask HandleAsync(TMessage message, OutboxDeliveryContext context, CancellationToken cancellationToken);
}
