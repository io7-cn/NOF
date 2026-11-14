using NOF.Infrastructure;
using NOF.Sample.Application;

namespace NOF.Sample.Services;

/// <summary>Forwards staged generic headers and the trusted delivery envelope while selecting native destinations through the application port.</summary>
public sealed class MessageOutboxHandler(IMessageClient client, MessageDelivery delivery) : IOutboxHandler<SendEmail>, IOutboxHandler<OrderCreated>
{
    public ValueTask HandleAsync(SendEmail message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => delivery.ExecuteAsync(message, context.Context,
            (headers, ct) => client.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, headers, ct),
            cancellationToken, context);

    public ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => delivery.ExecuteAsync(message, context.Context,
            (headers, ct) => client.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, headers, ct),
            cancellationToken, context);
}
