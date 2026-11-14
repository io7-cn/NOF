using NOF.Infrastructure;

namespace NOF.Sample.Messaging;

/// <summary>Infrastructure manually selects the queue and concrete RabbitMQ client.</summary>
public sealed class SendEmailOutboxHandler(RabbitMQDemoPublisher publisher) : IOutboxHandler<SendEmail>
{
    public async ValueTask HandleAsync(SendEmail message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => await publisher.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, context, cancellationToken);
}

/// <summary>One published intent is fanned out to independent durable subscriptions.</summary>
public sealed class OrderCreatedOutboxHandler(RabbitMQDemoPublisher publisher) : IOutboxHandler<OrderCreated>
{
    public async ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => await publisher.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, context, cancellationToken);
}
