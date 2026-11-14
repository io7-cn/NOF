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

ConfigureRabbitMQ is fluent and may be called repeatedly by feature modules before Build. Topology callbacks use native IChannel/CancellationToken and append in registration order. Framework StartingAsync completes every callback before consumers start, so applications implement no IHostedLifecycleService. Only MapQueue enables Inbox; topology-only/empty configuration does not. Queues must exist before consumption; MapQueue never infers or repairs exchange topology. Configuration freezes at startup; duplicates and later mutation are errors.

Use native ConnectionFactory settings for AMQP/AMQPS URI, authentication, TLS, timeouts and automatic recovery. RabbitMQOptions, its key/value ConnectionString parser and RabbitMQConnectionManager are removed. For custom QoS, pass a native configureChannel callback to MapQueue; receipt-storage failures requeue by default, with per-endpoint requeueOnFailure available.

Infrastructure Outbox handlers use RabbitMQConnection.GetAsync and native IConnection.CreateChannelAsync/CreateChannelOptions/BasicPublishAsync. Do not dispose the shared connection; dispose created channels. Wrap publishing with MessageDelivery.ExecuteAsync and the original OutboxDeliveryContext to preserve identity, tenant/trace and ordering. Use mandatory publication and native publisher confirms/tracking; propagate return/nack errors to Outbox retry. Application depends only on IOutbox and its owned ports.

AddRabbitMQBackplane() is independent and reuses connection registration; configure its native connection through AddRabbitMQ. See docs/en/design/rabbitmq-sample.md for both services, receipt queries, native declarations, and explicit migration instructions.
