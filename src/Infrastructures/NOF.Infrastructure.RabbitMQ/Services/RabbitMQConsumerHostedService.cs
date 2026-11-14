using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NOF.Infrastructure.RabbitMQ;

public class RabbitMQConsumerHostedService : IHostedService, IDisposable
{
    private readonly RabbitMQConnectionManager _connectionManager;
    private readonly IOptions<RabbitMQOptions> _options;
    private readonly RabbitMQEndpointRegistry _bindings;
    private readonly ILogger<RabbitMQConsumerHostedService> _logger;
    private readonly MessageInboxReceiver _receiver;
    private readonly List<IChannel> _channels = [];
    private bool _disposed;

    public RabbitMQConsumerHostedService(
        RabbitMQConnectionManager connectionManager,
        IOptions<RabbitMQOptions> options,
        RabbitMQEndpointRegistry bindings,
        MessageInboxReceiver receiver,
        ILogger<RabbitMQConsumerHostedService> logger)
    {
        _connectionManager = connectionManager;
        _options = options;
        _bindings = bindings;
        _receiver = receiver;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RegisterConsumersFromRegistryAsync(cancellationToken);
            _logger.LogInformation("RabbitMQ consumers initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ consumers");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private async Task RegisterConsumersFromRegistryAsync(CancellationToken cancellationToken)
    {
        foreach (var endpoint in _bindings.Endpoints)
        {
            var channel = await _connectionManager.CreateChannelAsync(cancellationToken: cancellationToken);
            _channels.Add(channel);
            await channel.BasicQosAsync(0, _options.Value.PrefetchCount, false, cancellationToken);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += async (_, args) => await HandleMessageAsync(endpoint.ConsumerId, consumer, args);
            // BasicConsume fails if the queue does not exist. NOF never declares or binds broker topology.
            await channel.BasicConsumeAsync(endpoint.QueueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);
        }
    }

    private async Task HandleMessageAsync(string consumerId, AsyncEventingBasicConsumer consumer, BasicDeliverEventArgs args)
    {
        try
        {
            var payload = args.Body;

            Dictionary<string, string?>? headers = null;
            if (args.BasicProperties.Headers is not null)
            {
                headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in args.BasicProperties.Headers)
                {
                    if (value is byte[] bytes)
                    {
                        headers[key] = System.Text.Encoding.UTF8.GetString(bytes);
                    }
                }
            }

            var messageId = ResolveMessageId(args.BasicProperties.MessageId, headers);
            await _receiver.ReceiveAsync(messageId, InboxMessageType.Command, payload, consumerId, headers, CancellationToken.None);

            await consumer.Channel.BasicAckAsync(args.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            var requeue = _options.Value.RequeueOnConsumerFailure;
            _logger.LogError(
                ex,
                "Error handling message from queue {QueueName}. Requeue: {Requeue}",
                consumerId,
                requeue);
            try
            {
                await consumer.Channel.BasicNackAsync(args.DeliveryTag, false, requeue);
            }
            catch
            {
                // Ignore
            }
        }
    }

    /// <summary>Uses a native GUID MessageId, with NOF envelope headers as a fallback.</summary>
    public static Guid ResolveMessageId(string? propertyMessageId, IEnumerable<KeyValuePair<string, string?>>? headers)
        => Guid.TryParse(propertyMessageId, out var id) ? id : MessageInboxReceiver.GetMessageId(headers);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var channel in _channels)
        {
            try
            {
                channel.Dispose();
            }
            catch
            {
                // Ignore
            }
        }

        _disposed = true;
    }
}
