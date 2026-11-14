using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;
using RabbitMQ.Client;

namespace NOF.Sample.Messaging;

/// <summary>Sample infrastructure combines NOF delivery metadata with native RabbitMQ publishing.</summary>
public sealed class RabbitMQDemoPublisher(RabbitMQConnectionManager connection, IObjectSerializer serializer, MessageDelivery delivery)
{
    public ValueTask PublishAsync<T>(string exchange, string routingKey, T message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => delivery.ExecuteAsync(message!, context.Context, async (headers, ct) =>
        {
            await using var channel = await connection.CreateChannelAsync(
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
