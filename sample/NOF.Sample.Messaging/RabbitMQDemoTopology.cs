using RabbitMQ.Client;

namespace NOF.Sample.Messaging;

/// <summary>Application-owned native declarations; ConfigureRabbitMQ provides their startup lifecycle.</summary>
public static class RabbitMQDemoTopology
{
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
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
}
