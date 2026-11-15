using NOF.Infrastructure;
using NOF.Sample.Application;

namespace NOF.Sample.Services;

/// <summary>Publishes order-created intents using native infrastructure routing.</summary>
public sealed class OrderCreatedOutboxHandler(IMessageClient client) : OutboxHandler<OrderCreated>
{

    /// <inheritdoc />
    public override ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => client.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, context.Headers, cancellationToken);
}
