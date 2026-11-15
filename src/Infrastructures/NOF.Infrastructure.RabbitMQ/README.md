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

Message endpoints directly execute their operation with restored Context and the existing policy/filter pipeline. Business code owns idempotency and commit boundaries; adapters own acknowledgement, retry and dead-letter behavior. NOF Inbox storage and workers are removed. AddOutbox explicitly enables outgoing intents only; configure flat OutboxOptions. See [transactional Outbox and migration](https://github.com/io7-cn/NOF/blob/main/docs/en/design/transactional-outbox.md).

RabbitMQConnection.GetAsync returns a shared native IConnection; callers must not dispose it. Create/dispose native channels with IConnection.CreateChannelAsync and native CreateChannelOptions. The owner is lazy, shares in-flight connections, retries failed initial connects, isolates caller cancellation and cancels/disposes during host disposal. Established connections use native recovery. Use IChannel.BasicPublishAsync with OutboxDeliveryContext.Headers directly for confirmed Outbox publication; context and managed headers are already prepared.

AddRabbitMQBackplane() registers only the independent transient IBackplane capability using the same connection. It retains dedicated nof.backplane.* fanout exchanges and exclusive subscriber queues, independent of operation consumers.

Message endpoints directly execute their operation with restored Context and the existing policy/filter pipeline. Business code owns idempotency and commit boundaries; adapters own acknowledgement, retry and dead-letter behavior. NOF Inbox storage and workers are removed. AddOutbox explicitly enables outgoing intents only; configure flat OutboxOptions. See [transactional Outbox and migration](https://github.com/io7-cn/NOF/blob/main/docs/en/design/transactional-outbox.md).
