using Microsoft.Extensions.Hosting;
using NOF.Infrastructure.RabbitMQ;
using RabbitMQ.Client;

namespace NOF.Sample.Messaging;

/// <summary>Application infrastructure declares native topology before any queue consumers start.</summary>
public sealed class RabbitMQDemoTopology(RabbitMQConnectionManager connection) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(RabbitMQDemoRoutes.OrderEventsExchange, ExchangeType.Topic,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);
        foreach (var queue in new[] { RabbitMQDemoRoutes.MailQueue, RabbitMQDemoRoutes.OrderAuditQueue, RabbitMQDemoRoutes.OrderMetricsQueue })
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        }
        foreach (var queue in new[] { RabbitMQDemoRoutes.OrderAuditQueue, RabbitMQDemoRoutes.OrderMetricsQueue })
        {
            await channel.QueueBindAsync(queue, RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey,
                cancellationToken: cancellationToken);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
