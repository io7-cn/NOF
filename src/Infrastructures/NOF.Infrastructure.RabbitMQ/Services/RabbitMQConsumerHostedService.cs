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
    private readonly Dictionary<string, string> _queueConsumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _subscriptionConsumers = new(StringComparer.Ordinal);
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
        var commandRegistrations = BuildCommandConsumerRegistrations(_bindings.Endpoints);

        foreach (var registration in commandRegistrations)
        {
            _queueConsumers[registration.QueueName] = registration.ConsumerId;
            await SetupCommandConsumerAsync(registration.QueueName, registration.MessageRoute, cancellationToken);
        }

        var notificationRegistrations = BuildNotificationConsumerRegistrations(_bindings.Endpoints);

        foreach (var registration in notificationRegistrations)
        {
            _subscriptionConsumers[registration.QueueName] = registration.ConsumerId;
            await SetupNotificationConsumerAsync(registration.QueueName, registration.NotificationTypes, cancellationToken);
        }
    }

    private async Task SetupCommandConsumerAsync(string queueName, string messageRoute, CancellationToken cancellationToken)
    {
        var channel = await _connectionManager.CreateChannelAsync();
        _channels.Add(channel);

        await RabbitMQTopology.DeclareSystemTopologyAsync(channel, cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: messageRoute,
            type: "direct",
            durable: _options.Value.Durable,
            autoDelete: _options.Value.AutoDelete,
            arguments: RabbitMQTopology.BuildBusinessExchangeArguments(),
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: _options.Value.Durable,
            exclusive: false,
            autoDelete: _options.Value.AutoDelete,
            arguments: RabbitMQTopology.BuildBusinessQueueArguments(),
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: queueName,
            exchange: messageRoute,
            routingKey: messageRoute,
            cancellationToken: cancellationToken);

        await channel.BasicQosAsync(0, _options.Value.PrefetchCount, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, args) => await HandleMessageAsync(queueName, (AsyncEventingBasicConsumer)sender, args);

        await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    private async Task SetupNotificationConsumerAsync(string queueName, IReadOnlyCollection<string> notificationTypes, CancellationToken cancellationToken)
    {
        if (notificationTypes.Count == 0)
        {
            return;
        }

        var channel = await _connectionManager.CreateChannelAsync();
        _channels.Add(channel);

        await RabbitMQTopology.DeclareSystemTopologyAsync(channel, cancellationToken);

        await channel.QueueDeclareAsync(
            queue: queueName,
            durable: _options.Value.Durable,
            exclusive: false,
            autoDelete: _options.Value.AutoDelete,
            arguments: RabbitMQTopology.BuildBusinessQueueArguments(),
            cancellationToken: cancellationToken);

        foreach (var notificationType in notificationTypes)
        {
            var exchangeName = notificationType;
            await channel.ExchangeDeclareAsync(
                exchange: exchangeName,
                type: "fanout",
                durable: _options.Value.Durable,
                autoDelete: _options.Value.AutoDelete,
                arguments: RabbitMQTopology.BuildBusinessExchangeArguments(),
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                queue: queueName,
                exchange: exchangeName,
                routingKey: string.Empty,
                cancellationToken: cancellationToken);
        }

        await channel.BasicQosAsync(0, _options.Value.PrefetchCount, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (sender, args) => await HandleMessageAsync(queueName, (AsyncEventingBasicConsumer)sender, args);

        await channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    private async Task HandleMessageAsync(string queueName, AsyncEventingBasicConsumer consumer, BasicDeliverEventArgs args)
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

            if (_subscriptionConsumers.TryGetValue(queueName, out var notificationConsumerId))
            {
                var messageId = ResolveMessageId(headers);
                await _receiver.ReceiveAsync(
                    messageId,
                    InboxMessageType.Notification,
                    payload,
                    notificationConsumerId,
                    headers,
                    CancellationToken.None);
            }
            else if (_queueConsumers.TryGetValue(queueName, out var commandConsumerId))
            {
                var messageId = ResolveMessageId(headers);
                await _receiver.ReceiveAsync(
                    messageId,
                    InboxMessageType.Command,
                    payload,
                    commandConsumerId,
                    headers,
                    CancellationToken.None);
            }
            else
            {
                throw new RabbitMQConsumerMessageException(
                    $"Cannot route queue '{queueName}'. No matching handler route registered.",
                    requeue: false);
            }

            await consumer.Channel.BasicAckAsync(args.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            var requeue = ShouldRequeue(ex, _options.Value);
            _logger.LogError(
                ex,
                "Error handling message from queue {QueueName}. Requeue: {Requeue}",
                queueName,
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

    internal static bool ShouldRequeue(Exception exception, RabbitMQOptions options)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(options);

        return exception is RabbitMQConsumerMessageException messageException
            ? messageException.Requeue
            : options.RequeueOnConsumerFailure;
    }

    /// <summary>Builds one competing consumer queue for each explicit target binding.</summary>
    public static IReadOnlyCollection<CommandConsumerRegistration> BuildCommandConsumerRegistrations(IEnumerable<RabbitMQEndpoint> registrations)
        => [.. registrations.Where(binding => !binding.IsSubscription)
            .Select(binding => new CommandConsumerRegistration(binding.Route, binding.ConsumerId, binding.QueueName))];

    /// <summary>Uses the stable queue route directly.</summary>
    public static string BuildCommandQueueName(string messageRoute) => RabbitMQTopology.BuildCommandQueueName(messageRoute);

    /// <summary>Builds independently durable subscriptions, unaffected by handler or deployment renames.</summary>
    public static IReadOnlyCollection<NotificationConsumerRegistration> BuildNotificationConsumerRegistrations(IEnumerable<RabbitMQEndpoint> registrations)
        => [.. registrations.Where(binding => binding.IsSubscription)
            .Select(binding => new NotificationConsumerRegistration(binding.QueueName, binding.ConsumerId, [binding.Route]))];

    private static Guid ResolveMessageId(Dictionary<string, string?>? headers)
    {
        if (headers is not null
            && headers.TryGetValue(NOFAbstractionConstants.Transport.Headers.MessageId, out var value)
            && Guid.TryParse(value, out var messageId))
        {
            return messageId;
        }

        return Guid.NewGuid();
    }

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
    /// <summary>Describes a RabbitMQ notification consumer registration.</summary>

    public sealed record NotificationConsumerRegistration(
        string QueueName,
        string ConsumerId,
        IReadOnlyCollection<string> NotificationTypes);
    /// <summary>Describes a RabbitMQ command consumer registration.</summary>

    public sealed record CommandConsumerRegistration(
        string MessageRoute,
        string ConsumerId,
        string QueueName);
}
