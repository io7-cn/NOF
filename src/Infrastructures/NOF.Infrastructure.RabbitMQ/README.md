# NOF.Infrastructure.RabbitMQ

Thin RabbitMQ queue-to-operation consumers with native connection/channel APIs and an optional transient backplane.

```csharp
services.AddRabbitMQ(factory => factory.Uri = new Uri(connectionString));

services.ConfigureRabbitMQ(rabbit => rabbit.Topology(async (channel, ct) =>
{
    await channel.ExchangeDeclareAsync("orders", ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
    await channel.QueueDeclareAsync("orders.audit", durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
    await channel.QueueBindAsync("orders.audit", "orders", "order.created", cancellationToken: ct);
}));

services.ConfigureRabbitMQ(rabbit => rabbit
    .MapQueue<OrderAudit.Record>("orders.audit", configureChannel: async (channel, ct) => await channel.BasicQosAsync(0, 8, false, ct)));
```

AddRabbitMQ registers native ConnectionFactory/IConnectionFactory and RabbitMQConnection only. Native configuration supports TLS, AMQP/AMQPS URI, credentials, timeouts and recovery without duplicate NOF options or a connection-string parser. Repeated Add calls retain one connection owner and compose native configuration.

ConfigureRabbitMQ can be called by multiple modules before Build. Fluent Topology callbacks append in registration order; MapQueue selects unique existing queues and Empty operations without declared failures. Startup freezes the snapshot, runs all native topology callbacks before any consumer, then starts consumers. A topology failure aborts startup. Applications write no host lifecycle service. Consumers infer no topology; a missing queue still fails startup. Topology-only configuration installs no Inbox models/stores/workers. Native topology is not automatically rolled back on startup failure.

Native channel setup and receipt-failure requeue behavior can be specified per MapQueue; default prefetch is 1 and requeue is true. Broker policies, bindings, DLX, publisher confirms and replay remain native. There is no MapSubscription, topic DSL, implicit forwarding topology or RabbitMQMessageClient. Inbox acknowledgement means durable acceptance, with business processing committed later by the shared Inbox worker.

RabbitMQConnection.GetAsync returns a shared native IConnection; callers must not dispose it. Create/dispose native channels with IConnection.CreateChannelAsync and native CreateChannelOptions. The owner is lazy, shares in-flight connections, retries failed initial connects, isolates caller cancellation and cancels/disposes during host disposal. Established connections use native recovery. Use IChannel.BasicPublishAsync and MessageDelivery.ExecuteAsync with the full OutboxDeliveryContext for confirmed Outbox publication.

AddRabbitMQBackplane() registers only the independent transient IBackplane capability using the same connection. It retains dedicated nof.backplane.* fanout exchanges and exclusive subscriber queues, independent of operation consumers.

Migration: replace RabbitMQOptions/ConnectionString with native ConnectionFactory.Uri or fields; replace RabbitMQConnectionManager with RabbitMQConnection.GetAsync plus native channel calls; split old AddRabbitMQ(options, endpoints) into AddRabbitMQ and repeatable ConfigureRabbitMQ calls. Move application lifecycle methods into Topology callbacks. Existing queue names and Inbox keys do not change in this configuration refinement; earlier subscription/forwarding upgrades still require the explicit data/topology migration in the [sample guide](../../../docs/en/design/rabbitmq-sample.md) ([简体中文](../../../docs/zh-CN/design/rabbitmq-sample.md)).
