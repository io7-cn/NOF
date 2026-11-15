# RabbitMQ routing across two NOF services

[简体中文](../../zh-CN/design/rabbitmq-sample.md) · [Transactional outbox](transactional-outbox.md)

The Aspire sample runs a producer (`NOF.Sample`) and a receiver (`NOF.Sample.Notifications`) against the same RabbitMQ resource. RabbitMQ was already declared by AppHost; it now carries the sample messages instead of the in-process memory client. The producer retains PostgreSQL, Redis and the existing UI/OIDC features. The receiver owns a separate SQLite database for its application-owned idempotent projections.

## Run and observe

From the repository root, with Docker running and the pinned .NET SDK installed:

```sh
dotnet run --project sample/NOF.Sample.AppHost
```

AppHost starts RabbitMQ and the receiving service before the producer. Open the producer's HTTP endpoint from the Aspire dashboard. Set `PRODUCER` and `RECEIVER` to the HTTP addresses shown for `NOF-Sample` and `sample-notifications` (their ports may vary).

```sh
curl -i -X POST "$PRODUCER/api/messages/orders" \
  -H 'Content-Type: application/json' -d '{"email":"demo@example.test"}'
# 202 Accepted
# {"orderId":"<generated-guid>"}

curl "$RECEIVER/api/messages/receipts?orderId=<generated-guid>"
```

Poll the second endpoint until its `items` contains three receipts: mail (kind 0), order audit (kind 1), and order metrics (kind 2). Without `orderId`, it returns the latest 100 receipts. Invalid email returns 400 and stages no messages. The demo accepts anonymous, host-tenant requests; it simulates an order submission and mail processing without persisting an order aggregate or sending real email.

Stop `sample-notifications` in the dashboard, submit another order, then restart it. After its native durable queue/exchange bindings have been established at least once, RabbitMQ retains queued work while it is offline. Poll the receipts endpoint again to see processing resume. A completely new queue cannot receive events published before it was bound; the initial AppHost dependency prevents that race in this demo.

## Where routing lives

Application stages two transport-independent intents in the same `IUnitOfWork` and saves once:

```csharp
await outbox.EnqueueAsync(new OrderCreated(orderId), context, cancellationToken: ct);
await outbox.EnqueueAsync(new SendEmail(email, "Created", "Order created.", OrderId: orderId), context, cancellationToken: ct);
await unitOfWork.SaveChangesAsync(ct);
```

The producer explicitly selects scoped infrastructure adapters and stable logical schemas:

```csharp
builder.Services.AddScoped<IMessageClient, RabbitMQMessageClient>();
builder.AddApplicationPart(typeof(SendEmailOutboxHandler).Assembly);
builder.Services.AddOutbox();
```

`NOF.Sample/Services/SendEmailOutboxHandler.cs` and `OrderCreatedOutboxHandler.cs` each derive from OutboxHandler<TMessage>, need no MessageType/SchemaVersion metadata, and are automatically registered by the application part. Each handler forwards the ready-to-send OutboxDeliveryContext.Headers and chooses destinations through Application.IMessageClient. The host implements that port in Services/RabbitMQMessageClient.cs, using native channel properties and confirmations (forward deliveryContext.Headers directly):

```csharp
await client.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, headers, ct);
await client.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, headers, ct);
```

The host client implementation uses native `IChannel.BasicPublishAsync`, not a NOF send/publish client. It creates a native channel with publisher confirmations/tracking enabled and publishes with `mandatory: true`. The Outbox handler forwards complete `deliveryContext.Headers`, including the persisted JSON context, original ID, tenant/trace and custom headers; native BasicProperties also receives MessageId. Return/nack errors propagate to the Outbox attempt. No exchange, queue or forwarding topology is declared while publishing.

The receiving host's `NotificationMessaging.cs` explicitly binds infrastructure routes to operations:

```csharp
builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(connectionString))
    .ConfigureRabbitMQ(NotificationMessaging.Configure);

// The feature module inlines its native topology and endpoint declarations.

// NotificationMessaging.Configure:
rabbit.Topology(async (channel, ct) =>
{
    // Native ExchangeDeclareAsync, QueueDeclareAsync and QueueBindAsync.
})
.MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue)
.MapQueue<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderAuditQueue)
.MapQueue<CountOrder>(RabbitMQDemoRoutes.OrderMetricsQueue);
```

| Intent | Physical destination | Consumer operation |
|---|---|---|
| `SendEmail` | queue `sample.mail.v1` | `SendMail` |
| `OrderCreated` | queue `sample.order-audit.v1` | `RecordOrder` |
| `OrderCreated` | queue `sample.order-metrics.v1` | `CountOrder` |

A queue distributes deliveries among competing consumers. `NotificationMessaging.Configure` directly includes the native topology lambda and fluent queue mappings. ConfigureRabbitMQ owns startup timing; applications implement no IHostedLifecycleService or separate topology holder. It declares the durable `sample.orders.v1` topic exchange and all three queues with RabbitMQ.Client, then binds both order queues with routing key `order.created`. The mail adapter publishes to the default exchange (`exchange: ""`) using the mail queue name as routing key. NOF knows none of these exchange relationships.

```csharp
await channel.ExchangeDeclareAsync(RabbitMQDemoRoutes.OrderEventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
await channel.QueueBindAsync(queue, RabbitMQDemoRoutes.OrderEventsExchange, "order.created", cancellationToken: ct);
```

Each bound order queue receives its own copy. Scaling consumers of the same queue adds competing workers, not additional recipients. Contract/Application remain transport independent. Routing, topology, serialization and native publish properties stay in sample infrastructure.

Both hosts call `AddRabbitMQ` with the `rabbitmq` connection string injected by Aspire. Queue mappings automatically assemble transactional messaging for the receiver, which stops Outbox workers with `Outbox.RunWorkers=false`. The producer calls `AddOutbox` and stops Inbox workers with `Inbox.RunWorkers=false`; it has no consumers. Both retain the complete message model, including the producer's existing PostgreSQL migration model. Receiver handlers stage projections through `INotificationReceipts`; they do not save independently. The Inbox worker commits those changes with its processed receipt in the same SQLite unit of work. Acknowledgement at the broker means durable Inbox acceptance, not completion of the business handler.

## Persistence and verification

The producer selects native EF/PostgreSQL, calls AddOutbox and stages both intents with the business unit of work. The receiver chooses native EF/SQLite for its own projection data; MapQueue enables no Outbox/Inbox persistence. Each operation directly awaits INotificationReceipts.RecordAsync. The port executes a parameterized SQLite INSERT ... ON CONFLICT DO NOTHING against a tenant-scoped unique (IdempotencyKey, Kind, TenantId) index. An order ID supplies the business key; mail without an order uses its stable message ID. Different transport IDs for the same order still yield one projection per kind. A separate query-before-insert is not used.

RabbitMQ ACK follows the operation's committed projection, not receipt acceptance. A commit/ACK failure can redeliver; the unique business key keeps the projection unchanged. The mail operation only simulates mail by recording a projection. A real SMTP/provider effect needs an Outbox/provider idempotency contract; recording a row alone cannot make that effect atomic.

The non-broker NotificationIdempotencyTests concurrently delivers duplicates and verifies exactly three business projections without framework receipt tables. RabbitMQDeliveryTests drives the native consumer callback with a mocked channel and verifies no early ACK, failure NACK and non-Guid ID propagation. Optional live tests exercise queue/fanout, offline recovery, complete headers, native/legacy IDs, and business deduplication. Set NOF_SAMPLE_RABBITMQ to an isolated connection string to run live tests; they are skipped when it is absent. These tests do not apply a production PostgreSQL migration.

## Adapter boundary and migration

AddRabbitMQ configures native ConnectionFactory; repeatable fluent ConfigureRabbitMQ appends topology callbacks and queue-to-operation mappings before Build. Framework StartingAsync completes topology before starting consumers. Only MapQueue registers consumers; topology-only/client-only hosts need no persistence. Queue mapping requires void, rejects duplicate queues and never creates topology implicitly. Native topology, prefetch, requeue and dead-letter policy belong to the host. Global/Service/UseCase authorization and filters still run before handler construction.

Each delivery restores Context in a fresh scope and executes the selected operation directly. No InboxEndpointRegistry, receipt route, claim, retry counter or consumer database exists in the framework. Missing transport MessageId stays missing; native IDs override the legacy header without Guid coercion. Direct Memory dispatch has the same execution boundary and propagates failure, but does not promise broker-like restart durability.

Before switching binaries, stop old consumers/Inbox workers and back up tenant databases. Resolve Pending/Failed work using the old runtime or explicitly export/replay reviewed records; old messages may already have been ACKed. Export completion history if needed. The producer's guarded 20261116000000_RemoveInbox migration refuses unfinished records and drops only the retired Inbox table, leaving Outbox and identity data intact. No live database changes are performed by the source update.

## Sample project boundaries

NOF.Sample.Notifications is one service project with its operations, application-owned projection port and native SQLite implementation. Shared payloads/routes remain in NOF.Sample.Contract; the publishing client interface belongs to Application and its native implementation/Outbox handlers to NOF.Sample. CountOrder remains an internal reaction expressed as a standalone OperationHandler, not a Contract method.

NotificationsDbContext uses EnsureCreated for a fresh local file; it does not upgrade existing files. An existing receiver needs a reviewed schema upgrade for IdempotencyKey and its unique index: derive order keys for known order projections, explicitly resolve duplicates, and decide keys for older mail lacking an order/message ID. Back up old Inbox history and resolve unfinished work before removing its table. Do not drop a populated file to simulate migration. The sample business row is observability/projection data, not a generic NOF Inbox replacement.
