# RabbitMQ routing across two NOF services

[简体中文](../../zh-CN/design/rabbitmq-sample.md) · [Transactional outbox](transactional-outbox.md)

The Aspire sample runs a producer (`NOF.Sample`) and a receiver (`NOF.Sample.Notifications`) against the same RabbitMQ resource. RabbitMQ was already declared by AppHost; it now carries the sample messages instead of the in-process memory client. The producer retains PostgreSQL, Redis and the existing UI/OIDC features. The receiver owns a separate SQLite database for its Inbox and queryable processing receipts.

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
builder.Services.AddOutbox(outbox =>
{
    outbox.Add<SendEmail, RabbitMQDemoPublisher>("sample.send-email", 1);
    outbox.Add<OrderCreated, RabbitMQDemoPublisher>("sample.order-created", 1);
});
```

`NOF.Sample.Messaging/RabbitMQDemoPublisher.cs` directly implements both typed Outbox handlers and chooses native exchange/routing-key pairs. No forwarding handler classes or extra publisher registration are needed:

```csharp
await publisher.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, deliveryContext, ct);
await publisher.PublishAsync(RabbitMQDemoRoutes.OrderEventsExchange, RabbitMQDemoRoutes.OrderCreatedRoutingKey, message, deliveryContext, ct);
```

The sample publisher uses native `IChannel.BasicPublishAsync`, not a NOF send/publish client. It creates a native channel with publisher confirmations/tracking enabled and publishes with `mandatory: true`. `MessageDelivery.ExecuteAsync(..., deliveryContext)` supplies the original Outbox ID, tenant/trace and ordering headers; native BasicProperties also receives MessageId. Return/nack errors propagate to the Outbox attempt. No exchange, queue or forwarding topology is declared while publishing.

The receiving host's `NotificationMessaging.cs` explicitly binds infrastructure routes to operations:

```csharp
builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(connectionString));

// The feature module inlines its native topology and endpoint declarations.
builder.Services.ConfigureRabbitMQ(NotificationMessaging.Configure);

// NotificationMessaging.Configure:
rabbit.Topology(async (channel, ct) =>
{
    // Native ExchangeDeclareAsync, QueueDeclareAsync and QueueBindAsync.
})
.MapQueue<MessageDemoService.SendMail>(RabbitMQDemoRoutes.MailQueue)
.MapQueue<MessageDemoService.RecordOrder>(RabbitMQDemoRoutes.OrderAuditQueue)
.MapQueue<MessageDemoService.CountOrder>(RabbitMQDemoRoutes.OrderMetricsQueue);
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

Both hosts call `AddRabbitMQ` with the `rabbitmq` connection string injected by Aspire. The receiver enables Inbox by configuring RabbitMQ endpoints. The producer has no consumers; it retains an inactive Inbox capability (`AddInbox`, `Inbox.RunWorkers=false`) solely to preserve its existing PostgreSQL migration model alongside Outbox. Receiver handlers stage projections through `INotificationReceipts`; they do not save independently. The Inbox worker commits those changes with its processed receipt in the same SQLite unit of work. Acknowledgement at the broker means durable Inbox acceptance, not completion of the business handler.

## Persistence and verification

The receiver uses `notifications.db` in its project working directory. NOF initializes a fresh SQLite schema on first use; this disposable demo has no migration chain. Stop the receiver before deleting its database to reset the demo. Production deployments need an explicit migration strategy. The existing producer PostgreSQL migrations and their upgrade precautions still apply; this sample adds no migration or automatic cleanup of existing data. `SendEmail.OrderId` is optional, preserving the earlier v1 payload shape for pending messages.

Delivery is at least once. NOF keeps the message identity and uses Inbox deduplication per route; the two event subscriptions deliberately have separate Inbox routes for the same message ID. External effects such as real SMTP delivery would still need their own idempotency design.

The normal sample tests skip the live-broker test unless `NOF_SAMPLE_RABBITMQ` is configured. Use an isolated broker/vhost: the test declares the sample's fixed durable route names. In a shell with that environment variable set to a connection string:

```sh
dotnet test sample-tests/NOF.Sample.Tests -c Release --filter FullyQualifiedName~RabbitMQDispatchTests
```

The test starts independent producer/receiver hosts and databases, exercises real RabbitMQ queue delivery and fanout, stops/restarts the receiver, and compares Inbox IDs with Outbox IDs. An explicitly configured but unavailable broker fails the test. CI runs it in a dedicated job with a disposable RabbitMQ service. PostgreSQL upgrade validation remains a separate deployment concern.

## Adapter boundary and migration

MessageBinding, MessageBindingRegistry and AddMessageBindings are removed. RabbitMQ exposes only MapQueue<Operation>: no topic/subscription DSL, automatic exchange/queue/binding declaration, alternate/dead-letter forwarding convention or original-route replay headers. RabbitMQMessageClient and command/notification Riders are removed; use native channels. Publisher confirmations are native CreateChannelOptions, not duplicate NOF options. A missing queue fails startup. An unroutable mandatory publish fails the delivery attempt. Broker policies, retries through DLX, exchange bindings and replay are application/operator responsibilities.

RabbitMQOptions and RabbitMQConnectionManager are removed. AddRabbitMQ accepts native ConnectionFactory configuration; use an AMQP/AMQPS URI (`factory.Uri`) or native fields rather than the old key/value ConnectionString parser. TLS, timeouts and automatic recovery remain native. RabbitMQConnection.GetAsync owns only lazy shared connection lifetime: callers create/dispose native channels, never the shared connection. Failed initial connects can retry; caller cancellation does not cancel other waiters; established connections retain native recovery.

ConfigureRabbitMQ is independent, fluent and repeatable before Build. Callbacks append in declaration order, queue identities must be unique, and startup freezes the shared builder in place. Framework StartingAsync awaits every topology callback before starting consumers, even when a later module supplies topology for earlier endpoints. A failed/canceled topology aborts startup; no automatic rollback is attempted. Startup failures release opened consumer channels. Topology-only configuration enables no Inbox storage/model/workers. Empty configuration performs no network I/O. AddRabbitMQ alone remains lazy and enables no lifecycle or durable workers.

MapQueue optionally accepts `configureChannel: async (channel, ct) => await channel.BasicQosAsync(0, 8, false, ct)` and `requeueOnFailure`; default consumer prefetch is 1 and receipt-storage failures requeue. These settings belong to the selected endpoint.

The common Inbox still resolves scoped IInboxHandler entries through native keyed DI using opaque receipt Route values. It persists/deduplicates and executes in the existing tenant scope and transaction; it never interprets topology. RabbitMQ queue keys are `rabbitmq:queue:<queue>`; Memory has its own independent endpoints and keys. Native GUID MessageId is preferred, with the NOF envelope header as fallback.

Before upgrading, stop/drain old consumers and Inbox work and back up data. Map old `queue:`/`subscription:` (including the previous `rabbitmq:subscription:`) receipt keys to the selected queue key, updating Inbox.Route and InboxOrderState.Route together. Retain IDs, completed deduplication history and stream progress. Drain or explicitly move deliveries from old subscription queues to the new order queues before retiring topology. Old queues may carry NOF-installed dead-letter arguments: compare declarations and intentionally retain/recreate them through native RabbitMQ setup while offline. NOF performs no automatic remapping, deletion, replay or topology repair. Outbox schemas stay v1; old payloads are not reinterpreted.

The builder is also the host-local configuration owner: RabbitMQConfiguration and RabbitMQConfigurationSnapshot are removed. Read-only views become fixed when startup freezes declarations. Consumer startup/delivery code is inline, while connection ownership remains separate because its lifetime differs from channels and message scopes.
