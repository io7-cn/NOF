using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Messaging;
using NOF.Sample.Notifications.Application;

namespace NOF.Sample.Notifications;

/// <summary>The receiving host explicitly chooses which operations consume each physical route.</summary>
public static class NotificationMessaging
{
    public static void Configure(RabbitMQBuilder rabbit)
    {
        rabbit.MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue,
                configureChannel: async (channel, ct) => await channel.BasicQosAsync(0, 8, false, ct))
            .MapQueue<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderAuditQueue)
            .MapQueue<MessageDemoService.CountOrder>(RabbitMQDemoRoutes.OrderMetricsQueue);
    }
}
