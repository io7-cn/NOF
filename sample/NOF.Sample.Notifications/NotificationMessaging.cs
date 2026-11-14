using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Messaging;
using NOF.Sample.Notifications.Application;
using RabbitMQ.Client;

namespace NOF.Sample.Notifications;

/// <summary>The receiving host explicitly chooses which operations consume each physical route.</summary>
public static class NotificationMessaging
{
    public static void Configure(RabbitMQBuilder rabbit)
    {
        rabbit.Topology(async (channel, cancellationToken) =>
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
        })
            .MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue,
                configureChannel: async (channel, ct) => await channel.BasicQosAsync(0, 8, false, ct))
            .MapQueue<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderAuditQueue)
            .MapQueue<MessageDemoService.CountOrder>(RabbitMQDemoRoutes.OrderMetricsQueue);
    }
}
