using NOF.Abstraction;
using RabbitMQ.Client;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>Publishes JSON messages with native mandatory routing and publisher confirmations, without declaring topology.</summary>
public sealed class RabbitMQMessageClient(RabbitMQConnection connection, IObjectSerializer serializer)
{
    /// <summary>Publishes one persistent JSON message with explicit routing and headers; broker rejection or unroutable delivery fails the call.</summary>
    public async ValueTask PublishAsync<T>(string exchange, string routingKey, T message,
        IReadOnlyDictionary<string, string?> headers, CancellationToken cancellationToken = default)
    {
        var nativeConnection = await connection.GetAsync(cancellationToken);
        await using var channel = await nativeConnection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = headers.GetValueOrDefault(NOFAbstractionConstants.Transport.Headers.MessageId),
            Headers = headers.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase)
        };
        // mandatory + publisher confirms makes an unroutable/nacked publish fail the Outbox attempt.
        await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties,
            serializer.Serialize(message, typeof(T)), cancellationToken);
    }
}
