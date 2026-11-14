using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;
using RabbitMQ.Client;

namespace NOF.Sample.Messaging;

/// <summary>Sample infrastructure combines NOF delivery metadata with native RabbitMQ publishing.</summary>
public sealed class RabbitMQDemoPublisher(RabbitMQConnection connection, IObjectSerializer serializer, MessageDelivery delivery) : IOutboxHandler<SendEmail>, IOutboxHandler<OrderCreated>
{
    public ValueTask HandleAsync(SendEmail message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, context, cancellationToken);

    public ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, context, cancellationToken);

    public ValueTask PublishAsync<T>(string exchange, string routingKey, T message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => delivery.ExecuteAsync(message!, context.Context, async (headers, ct) =>
        {
            var nativeConnection = await connection.GetAsync(ct);
            await using var channel = await nativeConnection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                MessageId = context.MessageId.ToString(),
                Headers = headers.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase)
            };
            // mandatory + publisher confirms makes an unroutable/nacked publish fail the Outbox attempt.
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties,
                serializer.Serialize(message, typeof(T)), ct);
        }, cancellationToken, context);
}
