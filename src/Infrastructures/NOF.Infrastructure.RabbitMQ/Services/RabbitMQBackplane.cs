using NOF.Application;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>
/// RabbitMQ-backed transient backplane using dedicated fanout exchanges.
/// </summary>
public sealed class RabbitMQBackplane : IBackplane
{
    private const string ExchangePrefix = "nof.backplane.";
    private readonly RabbitMQConnection _connection;
    private readonly IObjectSerializer _serializer;

    public RabbitMQBackplane(
        RabbitMQConnection connection,
        IObjectSerializer serializer)
    {
        _connection = connection;
        _serializer = serializer;
    }

    public async ValueTask PublishAsync<TPayload>(string channel, TPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(payload);

        var exchangeName = BuildExchangeName(channel);
        var payloadTypeName = typeof(TPayload).AssemblyQualifiedName ?? typeof(TPayload).FullName ?? typeof(TPayload).Name;
        var payloadBytes = _serializer.Serialize(payload, typeof(TPayload));

        var connection = await _connection.GetAsync(cancellationToken).ConfigureAwait(false);
        await using var rabbitChannel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        await rabbitChannel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var properties = new BasicProperties
        {
            Persistent = false,
            ContentType = "application/octet-stream",
            Type = payloadTypeName
        };

        await rabbitChannel.BasicPublishAsync(
            exchange: exchangeName,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: properties,
            body: payloadBytes,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IAsyncDisposable> SubscribeAsync<TPayload>(
        string channel,
        Func<TPayload, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);

        var exchangeName = BuildExchangeName(channel);
        var expectedPayloadTypeName = typeof(TPayload).AssemblyQualifiedName ?? typeof(TPayload).FullName ?? typeof(TPayload).Name;
        var rabbitChannel = await (await _connection.GetAsync(cancellationToken).ConfigureAwait(false)).CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        await rabbitChannel.ExchangeDeclareAsync(
            exchange: exchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var queue = await rabbitChannel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await rabbitChannel.QueueBindAsync(
            queue: queue.QueueName,
            exchange: exchangeName,
            routingKey: string.Empty,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(rabbitChannel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            if (!string.Equals(args.BasicProperties.Type, expectedPayloadTypeName, StringComparison.Ordinal))
            {
                return;
            }

            var payload = _serializer.Deserialize<TPayload>(args.Body.ToArray());
            if (payload is TPayload typedPayload)
            {
                await handler(typedPayload, CancellationToken.None).ConfigureAwait(false);
            }
        };

        var consumerTag = await rabbitChannel.BasicConsumeAsync(
            queue: queue.QueueName,
            autoAck: true,
            consumer: consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new RabbitMQBackplaneSubscription(rabbitChannel, consumerTag);
    }

    public static string BuildExchangeName(string channel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        return $"{ExchangePrefix}{channel}";
    }

    private sealed class RabbitMQBackplaneSubscription : IAsyncDisposable
    {
        private readonly IChannel _channel;
        private readonly string _consumerTag;
        private int _disposed;

        public RabbitMQBackplaneSubscription(IChannel channel, string consumerTag)
        {
            _channel = channel;
            _consumerTag = consumerTag;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            try
            {
                if (_channel.IsOpen)
                {
                    await _channel.BasicCancelAsync(_consumerTag, false).ConfigureAwait(false);
                }
            }
            finally
            {
                await _channel.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
