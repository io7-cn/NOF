using NOF.Infrastructure;
using NOF.Sample.Application;

namespace NOF.Sample.Services;

/// <summary>Publishes order-created intents using native infrastructure routing.</summary>
public sealed class OrderCreatedOutboxHandler(IMessageClient client, MessageDelivery delivery) : OutboxHandler<OrderCreated>
{
    /// <summary>The durable schema identity; preserve it when renaming this handler.</summary>
    public const string MessageType = "sample.order-created";

    /// <inheritdoc />
    public override ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => delivery.ExecuteAsync(message, context.Context,
            (headers, ct) => client.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, headers, ct),
            cancellationToken, context);
}
