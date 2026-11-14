namespace NOF.Sample.Messaging;

/// <summary>Physical routes agreed by the infrastructure adapters of these two sample hosts.</summary>
public static class RabbitMQDemoRoutes
{
    public const string MailQueue = "sample.mail.v1";
    public const string OrderCreatedTopic = "sample.orders.created.v1";
    public const string AuditSubscription = "sample-order-audit";
    public const string MetricsSubscription = "sample-order-metrics";
}
