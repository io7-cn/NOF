---
description: Configure native RabbitMQ connections, composable topology and operation consumers
---

# Add RabbitMQ Messaging

Install NOF.Infrastructure.RabbitMQ. Register native connection access separately from endpoint/topology configuration:

```csharp
services.AddRabbitMQ(factory => factory.Uri = new Uri(connectionString));
services.ConfigureRabbitMQ(rabbit => rabbit.Topology(DeclareNativeTopologyAsync));
services.ConfigureRabbitMQ(rabbit => rabbit.MapQueue<MailService.Send>("mail.send.v1"));
```

Messaging: endpoints directly execute the mapped OperationHandler in isolated scopes through OperationExecutor, restoring Context before constructors and preserving authorization/filters. No Inbox/store/entity/receipt registry or consumer worker ships. Business owns idempotency, saves and transactions; native adapters own ACK/NACK, retry/dead-letter and concurrency. RabbitMQ ACK follows completed execution, not acceptance. Memory awaits handlers and propagates failures without process durability. MapQueue/AddMemoryMessaging never enable persistence. AddOutbox explicitly enables producer staging/workers; configure flat OutboxOptions and retain generated OutboxHandler<T> discovery. Resolve legacy Inbox Pending/Failed work with old binaries or explicit reviewed export/replay before guarded RemoveInbox; preserve required historical receipts via backup. See docs/en/design/transactional-outbox.md.

Use native ConnectionFactory settings for AMQP/AMQPS URI, authentication, TLS, timeouts and automatic recovery. RabbitMQOptions, its key/value ConnectionString parser and RabbitMQConnectionManager are removed. For custom QoS, pass a native configureChannel callback to MapQueue; receipt-storage failures requeue by default, with per-endpoint requeueOnFailure available.

Infrastructure Outbox handlers use RabbitMQConnection.GetAsync and native IConnection.CreateChannelAsync/CreateChannelOptions/BasicPublishAsync. Do not dispose the shared connection; dispose created channels. Forward the already complete OutboxDeliveryContext.Headers directly to native properties; the framework snapshots and restores Context/identity/tenant/trace without a delivery helper. Use mandatory publication and native publisher confirms/tracking; propagate return/nack errors to Outbox retry. Application depends only on IOutbox and its owned ports.

AddRabbitMQBackplane() is independent and reuses connection registration; configure its native connection through AddRabbitMQ. See docs/en/design/rabbitmq-sample.md for both services, receipt queries, native declarations, and explicit migration instructions.
