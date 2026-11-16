using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;

namespace NOF.Sample.Services;

/// <summary>Publishes order-created intents using native infrastructure routing.</summary>
public sealed class OrderCreatedOutboxHandler(RabbitMQMessageClient client, DemoDeliveryControl control) : OutboxHandler<OrderCreated>
{

    /// <inheritdoc />
    public override async ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext context, CancellationToken cancellationToken)
    {
        await control.BeforeDeliveryAsync(cancellationToken);
        await client.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, context.Headers, cancellationToken);
    }
}
