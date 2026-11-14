# NOF.Infrastructure.RabbitMQ

A thin adapter from existing RabbitMQ queues to NOF application operations, plus native connection access and an optional IBackplane implementation.

## Queue consumers

```csharp
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure.RabbitMQ;

var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.Services.AddRabbitMQ(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("rabbitmq");
    options.PrefetchCount = 8;
    options.RequeueOnConsumerFailure = true;
}, endpoints =>
{
    endpoints.MapQueue<MailService.Send>("mail.send.v1");
    endpoints.MapQueue<OrderAudit.Record>("orders.audit.v1");
});
```

MapQueue selects one Empty-returning operation without declared failures. The queue must already exist. NOF consumes it, durably accepts deliveries into Inbox, and acknowledges acceptance; the Inbox worker later executes the operation through the shared policies and transaction. Missing queues fail startup. Technical receipt-storage failures requeue by default. Payload validation and business execution failures follow the Inbox processing policy.

Connection-only AddRabbitMQ(options) enables no consumers or Inbox workers. Memory endpoints are separate. Core Inbox resolves scoped keyed IInboxHandler entries using opaque queue receipt keys; it has no shared MessageBinding or topology registry.

## Native topology and publishing

Application infrastructure or broker provisioning owns ExchangeDeclareAsync, QueueDeclareAsync, QueueBindAsync, broker policies, DLX and replay. The operation adapter declares no topology and offers no MapSubscription, topic convention, forwarding exchange or original-route headers.

RabbitMQConnectionManager.GetConnectionAsync returns the shared native connection (do not dispose it); CreateChannelAsync returns a caller-owned IChannel and accepts native CreateChannelOptions plus cancellation. Use BasicPublishAsync directly. For confirmed Outbox delivery, configure publisher confirmations/tracking natively, use mandatory publishing, and wrap dispatch in MessageDelivery.ExecuteAsync with the original OutboxDeliveryContext. Set native MessageId from that same ID. Exceptions must propagate to the Outbox retry policy. RabbitMQMessageClient and command/notification Riders are removed.

ConnectionString accepts AMQP URI or key/value form. Individual HostName, Port, UserName, Password and VirtualHost remain available. Publisher settings and topology durability/auto-delete flags are no longer duplicated in NOF options; native RabbitMQ calls specify them.

## Optional backplane

AddRabbitMQBackplane configures the separate transient IBackplane capability. It owns dedicated nof.backplane.* fanout exchanges and exclusive auto-delete subscriber queues; it is independent of application queue-to-operation mapping and is not used by the sample's order workflow.

See the [complete two-service sample and explicit migration](../../../docs/en/design/rabbitmq-sample.md) ([简体中文](../../../docs/zh-CN/design/rabbitmq-sample.md)). Upgrades must explicitly drain/move old subscription queues, migrate Inbox/order-state keys while preserving deduplication history, and reconcile old queue arguments. NOF never deletes or silently repairs broker or database state.

## Dependencies

- [NOF.Infrastructure](https://www.nuget.org/packages/NOF.Infrastructure)
- [RabbitMQ.Client](https://www.nuget.org/packages/RabbitMQ.Client)
