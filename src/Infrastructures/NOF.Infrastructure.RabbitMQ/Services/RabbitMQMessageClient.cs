using NOF.Abstraction;
using RabbitMQ.Client;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>Publishes JSON messages with native mandatory routing and publisher confirmations, without declaring topology.</summary>
public sealed class RabbitMQMessageClient(RabbitMQConnection connection, IObjectSerializer serializer)
{
    /// <summary>Publishes one persistent JSON message and awaits native publisher confirmation.</summary>
    /// <typeparam name="T">The declared payload type used for JSON metadata.</typeparam>
    /// <param name="exchange">The native exchange name; an empty string selects the default exchange.</param>
    /// <param name="routingKey">The native routing key, interpreted by the configured exchange topology.</param>
    /// <param name="message">The message payload.</param>
    /// <param name="headers">Prepared transport headers, including any durable context and message identity.</param>
    /// <param name="cancellationToken">Cancels connection waiting and native channel operations.</param>
    /// <returns>Completion after the confirmed publication and disposal of the call-owned channel.</returns>
    /// <remarks>
    /// Declares no topology and never disposes the shared connection. Serialization failures, broker rejection
    /// and mandatory returns propagate to the caller so an Outbox handler can fail its current attempt.
    /// </remarks>
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
