using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NOF.Infrastructure.RabbitMQ;

public class RabbitMQConsumerHostedService : IHostedLifecycleService, IDisposable
{
    private readonly RabbitMQConnection _connection;
    private readonly RabbitMQBuilder _configuration;
    private readonly ILogger<RabbitMQConsumerHostedService> _logger;
    private readonly IServiceProvider _services;
    private readonly List<IChannel> _channels = [];
    private readonly CancellationTokenSource _stopping = new();
    private bool _disposed;

    public RabbitMQConsumerHostedService(
        RabbitMQConnection connection,
        RabbitMQBuilder configuration,
        IServiceProvider services,
        ILogger<RabbitMQConsumerHostedService> logger)
    {
        _connection = connection;
        _configuration = configuration;
        _services = services;
        _logger = logger;
    }

    /// <summary>Executes all user topology callbacks before native host StartAsync starts consumers.</summary>
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var configuration = _configuration.Freeze();
        if (configuration.Topologies.Count == 0)
        {
            return;
        }
        var connection = await _connection.GetAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        foreach (var configure in configuration.Topologies)
        {
            await configure(channel, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var endpoint in _configuration.Freeze().Endpoints)
            {
                var connection = await _connection.GetAsync(cancellationToken);
                var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                _channels.Add(channel);
                await channel.BasicQosAsync(0, 1, false, cancellationToken);
                if (endpoint.ConfigureChannel is not null)
                {
                    await endpoint.ConfigureChannel(channel, cancellationToken);
                }
                var consumer = new AsyncEventingBasicConsumer(channel);
                var stoppingToken = _stopping.Token;
                consumer.ReceivedAsync += async (_, args) =>
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
                                else if (value is null or string)
                                {
                                    headers[key] = (string?)value;
                                }
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(args.BasicProperties.MessageId))
                        {
                            headers ??= new(StringComparer.OrdinalIgnoreCase);
                            headers[NOFAbstractionConstants.Transport.Headers.MessageId] = args.BasicProperties.MessageId;
                        }
                        await _services.ExecuteMessageAsync(endpoint.Operation, payload, headers, stoppingToken);
                        stoppingToken.ThrowIfCancellationRequested();

                        await consumer.Channel.BasicAckAsync(args.DeliveryTag, false);
                    }
                    catch (Exception ex)
                    {
                        var requeue = endpoint.RequeueOnFailure;
                        _logger.LogError(
                            ex,
                            "Error handling message from queue {QueueName}. Requeue: {Requeue}",
                            endpoint.QueueName,
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
                };
                // BasicConsume fails if the queue does not exist. The consumer never infers broker topology.
                await channel.BasicConsumeAsync(endpoint.QueueName, autoAck: false, consumer: consumer, cancellationToken: cancellationToken);
            }
            _logger.LogInformation("RabbitMQ consumers initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ consumers");
            Dispose();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping.Cancel();
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

        _stopping.Dispose();
    }
}
