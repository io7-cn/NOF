namespace NOF.Sample;

/// <summary>Application-owned physical RabbitMQ names; NOF has no topic/subscription convention.</summary>
public static class RabbitMQDemoRoutes
{
    public const string MailQueue = "sample.mail.v1";
    public const string OrderAuditQueue = "sample.order-audit.v1";
    public const string OrderMetricsQueue = "sample.order-metrics.v1";
    public const string OrderEventsExchange = "sample.orders.v1";
    public const string OrderCreatedRoutingKey = "order.created";
}
