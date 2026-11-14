using NOF.Abstraction;
using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Application;
using RabbitMQ.Client;

namespace NOF.Sample.Services;

/// <summary>Sample infrastructure combines NOF delivery metadata with native RabbitMQ publishing.</summary>
public sealed class RabbitMQMessageClient(RabbitMQConnection connection, IObjectSerializer serializer) : IMessageClient
{
    public async ValueTask PublishAsync<T>(string exchange, string routingKey, T message,
        IReadOnlyDictionary<string, string?> headers, CancellationToken cancellationToken)
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
