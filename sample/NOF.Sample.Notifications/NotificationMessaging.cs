using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Messaging;
using NOF.Sample.Notifications.Application;

namespace NOF.Sample.Notifications;

/// <summary>The receiving host explicitly chooses which operations consume each physical route.</summary>
public static class NotificationMessaging
{
    public static void Configure(RabbitMQEndpointsBuilder bindings)
    {
        bindings.MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue);
        bindings.MapQueue<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderAuditQueue);
        bindings.MapQueue<MessageDemoService.CountOrder>(RabbitMQDemoRoutes.OrderMetricsQueue);
    }
}
