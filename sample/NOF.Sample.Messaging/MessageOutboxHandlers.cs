using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;

namespace NOF.Sample.Messaging;

/// <summary>Infrastructure manually selects the queue and concrete RabbitMQ client.</summary>
public sealed class SendEmailOutboxHandler(RabbitMQMessageClient client) : IOutboxHandler<SendEmail>
{
    public async ValueTask HandleAsync(SendEmail message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => await client.SendAsync(RabbitMQDemoRoutes.MailQueue, message, context, cancellationToken);
}

/// <summary>One published intent is fanned out to independent durable subscriptions.</summary>
public sealed class OrderCreatedOutboxHandler(RabbitMQMessageClient client) : IOutboxHandler<OrderCreated>
{
    public async ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => await client.PublishAsync(RabbitMQDemoRoutes.OrderCreatedTopic, message, context, cancellationToken);
}
